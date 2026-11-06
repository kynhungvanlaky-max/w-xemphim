using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/telemetry")]
public sealed class AdminTelemetryController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private const int MaxSummaryHours = 168;
    private const int MaxStartupSamplesForPercentile = 50_000;

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AdminTelemetrySummaryDto>>> GetSummary([FromQuery] int hours = 24)
    {
        hours = Math.Clamp(hours, 1, MaxSummaryHours);
        var toUtc = UtcNow;
        var fromUtc = toUtc.AddHours(-hours);

        // Mỗi nhóm truy vấn dùng DbContext riêng và chạy song song (DbContext không thread-safe).
        var countsTask = LoadCountsAsync();
        var startupTask = LoadStartupStatsAsync();
        var timelineTask = LoadTimelineAsync();
        var eventTypesTask = LoadEventTypesAsync();
        var topSourcesTask = LoadTopSourcesAsync();
        var recentErrorsTask = LoadRecentErrorsAsync();

        await Task.WhenAll(countsTask, startupTask, timelineTask, eventTypesTask, topSourcesTask, recentErrorsTask);

        var counts = await countsTask;
        var startup = await startupTask;
        var timeline = await timelineTask;
        var eventTypes = await eventTypesTask;
        var topSources = await topSourcesTask;
        var recentErrors = await recentErrorsTask;

        var summary = new AdminTelemetrySummaryDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Hours = hours,
            TotalEvents = counts.TotalEvents,
            Sessions = counts.Sessions,
            StartupSamples = startup.Samples,
            AvgStartupMs = startup.Avg,
            P95StartupMs = startup.P95,
            RebufferEvents = counts.RebufferEvents,
            AvgRebufferMs = Math.Round(counts.AvgRebufferMs, 1),
            RebufferRatePerSession = counts.Sessions == 0 ? 0 : Math.Round((double)counts.RebufferEvents / counts.Sessions, 2),
            ErrorEvents = counts.ErrorEvents,
            ErrorRatePerSession = counts.Sessions == 0 ? 0 : Math.Round((double)counts.ErrorEvents / counts.Sessions, 2),
            AvgBitrateBps = (long)Math.Round(counts.AvgBitrateBps),
            Timeline = timeline.Select(bucket => new AdminTelemetryBucketDto
            {
                BucketStartUtc = bucket.Date.AddHours(bucket.Hour),
                Events = bucket.Events,
                Errors = bucket.Errors,
                Rebuffers = bucket.Rebuffers,
                AvgStartupMs = bucket.StartupCount == 0 ? 0 : Math.Round((double)bucket.StartupSum / bucket.StartupCount, 1)
            }).ToList(),
            EventTypes = eventTypes,
            TopSources = topSources.Select(source => new AdminTelemetrySourceStatDto
            {
                ServerName = source.ServerName,
                Events = source.Events,
                Errors = source.Errors,
                ErrorRate = source.Events == 0 ? 0 : Math.Round((double)source.Errors / source.Events, 3)
            }).ToList(),
            RecentErrors = recentErrors.Select(MapEvent).ToList()
        };

        return Ok(ApiResponse<AdminTelemetrySummaryDto>.Ok(summary));

        async Task<(int TotalEvents, int Sessions, int RebufferEvents, int ErrorEvents, double AvgRebufferMs, double AvgBitrateBps)> LoadCountsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            var q = c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc);
            var totalEvents = await q.CountAsync();
            var sessions = await q
                .Where(item => item.SessionId != null && item.SessionId != string.Empty)
                .Select(item => item.SessionId)
                .Distinct()
                .CountAsync();
            var rebufferEvents = await q.CountAsync(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer);
            var errorEvents = await q.CountAsync(item => item.EventType == PlaybackTelemetryEventTypes.Error);
            var avgRebufferMs = await q
                .Where(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer)
                .AverageAsync(item => (double?)item.Value) ?? 0;
            var avgBitrateBps = await q
                .Where(item => item.EventType == PlaybackTelemetryEventTypes.Bitrate && item.Value > 0)
                .AverageAsync(item => (double?)item.Value) ?? 0;
            return (totalEvents, sessions, rebufferEvents, errorEvents, avgRebufferMs, avgBitrateBps);
        }

        async Task<(int Samples, double Avg, long P95)> LoadStartupStatsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            if (c.Database.IsNpgsql())
            {
                // P95 chính xác bằng percentile_cont ngay trong PostgreSQL (không kéo mẫu về app).
                var row = await c.Database.SqlQueryRaw<StartupStatsRow>(
                    "SELECT COUNT(*)::bigint AS \"Count\", COALESCE(AVG(\"Value\"::double precision), 0) AS \"Avg\", COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY \"Value\"::double precision), 0) AS \"P95\" FROM playback_telemetry WHERE \"EventType\" = {0} AND \"CreatedAtUtc\" >= {1} AND \"CreatedAtUtc\" <= {2}",
                    PlaybackTelemetryEventTypes.Startup, fromUtc, toUtc).SingleAsync();
                return ((int)row.Count, Math.Round(row.Avg, 1), (long)Math.Round(row.P95));
            }

            var startupValues = await c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc
                            && item.EventType == PlaybackTelemetryEventTypes.Startup)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => item.Value)
                .Take(MaxStartupSamplesForPercentile)
                .ToListAsync();
            return (startupValues.Count,
                startupValues.Count == 0 ? 0 : Math.Round(startupValues.Average(), 1),
                CalculateP95(startupValues));
        }

        async Task<List<TimelineRow>> LoadTimelineAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc)
                .GroupBy(item => new { item.CreatedAtUtc.Date, item.CreatedAtUtc.Hour })
                .Select(group => new TimelineRow
                {
                    Date = group.Key.Date,
                    Hour = group.Key.Hour,
                    Events = group.Count(),
                    Errors = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Error ? 1 : 0),
                    Rebuffers = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer ? 1 : 0),
                    StartupSum = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Startup ? item.Value : 0),
                    StartupCount = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Startup ? 1 : 0)
                })
                .OrderBy(bucket => bucket.Date)
                .ThenBy(bucket => bucket.Hour)
                .ToListAsync();
        }

        async Task<List<AdminTelemetryEventTypeCountDto>> LoadEventTypesAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc)
                .GroupBy(item => item.EventType)
                .Select(group => new AdminTelemetryEventTypeCountDto
                {
                    EventType = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ToListAsync();
        }

        async Task<List<TopSourceRow>> LoadTopSourcesAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc
                            && item.ServerName != null && item.ServerName != string.Empty)
                .GroupBy(item => item.ServerName!)
                .Select(group => new TopSourceRow
                {
                    ServerName = group.Key,
                    Events = group.Count(),
                    Errors = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Error ? 1 : 0)
                })
                .OrderByDescending(item => item.Events)
                .Take(8)
                .ToListAsync();
        }

        async Task<List<PlaybackTelemetryEntity>> LoadRecentErrorsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.PlaybackTelemetry.AsNoTracking()
                .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc
                            && item.EventType == PlaybackTelemetryEventTypes.Error)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(10)
                .ToListAsync();
        }
    }

    [HttpGet("events")]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminTelemetryEventDto>>>> GetEvents(
        [FromQuery] string? eventType = null,
        [FromQuery] int? episodeId = null,
        [FromQuery] string? sessionId = null,
        [FromQuery] int hours = 24,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        hours = Math.Clamp(hours, 1, MaxSummaryHours);
        var pagination = Pagination.Normalize(page, pageSize, defaultPageSize: 50, maxPageSize: 200);
        page = pagination.Page;
        pageSize = pagination.PageSize;
        var fromUtc = UtcNow.AddHours(-hours);

        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.PlaybackTelemetry
            .AsNoTracking()
            .Where(item => item.CreatedAtUtc >= fromUtc);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var normalized = eventType.Trim().ToLowerInvariant();
            query = query.Where(item => item.EventType == normalized);
        }

        if (episodeId is > 0)
        {
            query = query.Where(item => item.EpisodeId == episodeId);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var normalizedSession = sessionId.Trim();
            query = query.Where(item => item.SessionId == normalizedSession);
        }

        var totalItems = await query.CountAsync();
        var entities = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .Skip(pagination.Skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<AdminTelemetryEventDto>>.Ok(new PagedResult<AdminTelemetryEventDto>
        {
            Items = entities.Select(MapEvent).ToList(),
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize
        }));
    }

    private sealed class TimelineRow
    {
        public DateTime Date { get; set; }
        public int Hour { get; set; }
        public int Events { get; set; }
        public int Errors { get; set; }
        public int Rebuffers { get; set; }
        public long StartupSum { get; set; }
        public int StartupCount { get; set; }
    }

    private sealed class TopSourceRow
    {
        public string ServerName { get; set; } = string.Empty;
        public int Events { get; set; }
        public int Errors { get; set; }
    }

    private sealed class StartupStatsRow
    {
        public long Count { get; set; }
        public double Avg { get; set; }
        public double P95 { get; set; }
    }

    private static AdminTelemetryEventDto MapEvent(PlaybackTelemetryEntity item) => new()
    {
        Id = item.Id,
        CreatedAtUtc = item.CreatedAtUtc,
        UserId = item.UserId,
        MovieId = item.MovieId,
        EpisodeId = item.EpisodeId,
        SessionId = item.SessionId,
        Position = item.Position,
        ServerName = item.ServerName,
        Quality = item.Quality,
        EventType = item.EventType,
        Value = item.Value,
        ErrorCode = item.ErrorCode
    };

    private static long CalculateP95(List<long> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var index = (int)Math.Ceiling(values.Count * 0.95) - 1;
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }
}
