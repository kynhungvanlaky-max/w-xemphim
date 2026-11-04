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

        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.PlaybackTelemetry
            .AsNoTracking()
            .Where(item => item.CreatedAtUtc >= fromUtc && item.CreatedAtUtc <= toUtc);

        var totalEvents = await query.CountAsync();
        var sessions = await query
            .Where(item => item.SessionId != null && item.SessionId != string.Empty)
            .Select(item => item.SessionId)
            .Distinct()
            .CountAsync();

        var startupValues = await query
            .Where(item => item.EventType == PlaybackTelemetryEventTypes.Startup)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => item.Value)
            .Take(MaxStartupSamplesForPercentile)
            .ToListAsync();

        var rebufferEvents = await query.CountAsync(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer);
        var errorEvents = await query.CountAsync(item => item.EventType == PlaybackTelemetryEventTypes.Error);
        var avgRebufferMs = await query
            .Where(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer)
            .AverageAsync(item => (double?)item.Value) ?? 0;
        var avgBitrateBps = await query
            .Where(item => item.EventType == PlaybackTelemetryEventTypes.Bitrate && item.Value > 0)
            .AverageAsync(item => (double?)item.Value) ?? 0;

        var timeline = await query
            .GroupBy(item => new { item.CreatedAtUtc.Date, item.CreatedAtUtc.Hour })
            .Select(group => new
            {
                group.Key.Date,
                group.Key.Hour,
                Events = group.Count(),
                Errors = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Error ? 1 : 0),
                Rebuffers = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Rebuffer ? 1 : 0),
                StartupSum = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Startup ? item.Value : 0),
                StartupCount = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Startup ? 1 : 0)
            })
            .OrderBy(bucket => bucket.Date)
            .ThenBy(bucket => bucket.Hour)
            .ToListAsync();

        var eventTypes = await query
            .GroupBy(item => item.EventType)
            .Select(group => new AdminTelemetryEventTypeCountDto
            {
                EventType = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ToListAsync();

        var topSources = await query
            .Where(item => item.ServerName != null && item.ServerName != string.Empty)
            .GroupBy(item => item.ServerName!)
            .Select(group => new
            {
                ServerName = group.Key,
                Events = group.Count(),
                Errors = group.Sum(item => item.EventType == PlaybackTelemetryEventTypes.Error ? 1 : 0)
            })
            .OrderByDescending(item => item.Events)
            .Take(8)
            .ToListAsync();

        var recentErrors = await query
            .Where(item => item.EventType == PlaybackTelemetryEventTypes.Error)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(10)
            .ToListAsync();

        var summary = new AdminTelemetrySummaryDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Hours = hours,
            TotalEvents = totalEvents,
            Sessions = sessions,
            StartupSamples = startupValues.Count,
            AvgStartupMs = startupValues.Count == 0 ? 0 : Math.Round(startupValues.Average(), 1),
            P95StartupMs = CalculateP95(startupValues),
            RebufferEvents = rebufferEvents,
            AvgRebufferMs = Math.Round(avgRebufferMs, 1),
            RebufferRatePerSession = sessions == 0 ? 0 : Math.Round((double)rebufferEvents / sessions, 2),
            ErrorEvents = errorEvents,
            ErrorRatePerSession = sessions == 0 ? 0 : Math.Round((double)errorEvents / sessions, 2),
            AvgBitrateBps = (long)Math.Round(avgBitrateBps),
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
            RecentErrors = recentErrors
                .Select(MapEvent)
                .ToList()
        };

        return Ok(ApiResponse<AdminTelemetrySummaryDto>.Ok(summary));
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
