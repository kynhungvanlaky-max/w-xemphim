using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin")]
public sealed class AdminEpisodesController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null,
    IHubContext<CinemaHub>? hubContext = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet("movies/{id:int}/episodes")]
    public async Task<ActionResult<ApiResponse<List<EpisodeDto>>>> GetEpisodes(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var episodes = await context.Episodes
            .Where(e => e.MovieId == id)
            .OrderBy(e => e.EpisodeNumber)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                MovieId = e.MovieId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                ThumbnailUrl = e.ThumbnailUrl,
                Overview = e.Overview
            })
            .ToListAsync();

        return Ok(ApiResponse<List<EpisodeDto>>.Ok(episodes));
    }

    [HttpPost("movies/{id:int}/episodes")]
    public async Task<ActionResult<ApiResponse<EpisodeDto>>> AddEpisode(int id, [FromBody] UpsertEpisodeRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null)
        {
            return NotFound(ApiResponse<EpisodeDto>.Fail($"Không tìm thấy phim với ID: {id}"));
        }

        var episodeNumber = request.EpisodeNumber;
        if (await context.Episodes.AnyAsync(e => e.MovieId == id && e.EpisodeNumber == episodeNumber))
        {
            var maxEp = await context.Episodes.Where(e => e.MovieId == id).MaxAsync(e => (int?)e.EpisodeNumber) ?? 0;
            episodeNumber = maxEp + 1;
        }

        var episode = new EpisodeEntity
        {
            MovieId = id,
            EpisodeNumber = episodeNumber,
            Title = string.IsNullOrWhiteSpace(request.Title) ? $"Tập {episodeNumber}" : request.Title.Trim(),
            DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 45,
            ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? movie.BackdropUrl : request.ThumbnailUrl.Trim(),
            Overview = string.IsNullOrWhiteSpace(request.Overview) ? $"Nội dung tập {episodeNumber}." : request.Overview.Trim()
        };

        var streamUrl = string.IsNullOrWhiteSpace(request.StreamUrl)
            ? "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
            : request.StreamUrl.Trim();

        episode.Streams.Add(new StreamSourceEntity
        {
            Position = 1,
            ServerName = string.IsNullOrWhiteSpace(request.ServerName) ? "VIP Server" : request.ServerName.Trim(),
            ServerType = request.ServerType,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
            StreamUrl = streamUrl
        });

        context.Episodes.Add(episode);

        if (episodeNumber > movie.CurrentEpisode) movie.CurrentEpisode = episodeNumber;
        if (episodeNumber > movie.TotalEpisodes) movie.TotalEpisodes = episodeNumber;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "ADD_EPISODE", "Episode", episode.Id.ToString(), $"Thêm tập {episodeNumber} cho phim '{movie.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        List<NotificationEntity> notificationsToBroadcast = [];
        try
        {
            var interestedUsers = await (from lib in context.UserLibrary
                                         where lib.MovieId == id
                                         join s in context.UserSettings on lib.UserId equals s.UserId into settingsGroup
                                         from s in settingsGroup.DefaultIfEmpty()
                                         where s == null || s.NotifyNewEpisodes
                                         select lib.UserId).Distinct().ToListAsync();

            if (interestedUsers.Count > 0)
            {
                notificationsToBroadcast = interestedUsers.Select(uid => new NotificationEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    Title = $"Tập mới: {movie.Title}",
                    Copy = $"Tập {episodeNumber} ({episode.Title}) vừa được phát hành.",
                    Icon = "play",
                    ActionUrl = $"/watch/{movie.Id}?ep={episodeNumber}",
                    Read = false,
                    CreatedAtUtc = UtcNow
                }).ToList();

                context.Notifications.AddRange(notificationsToBroadcast);
                await context.SaveChangesAsync();
            }
        }
        catch
        {
            // Non-critical notification generation should not block episode creation
        }

        if (hubContext != null && notificationsToBroadcast.Count > 0)
        {
            foreach (var n in notificationsToBroadcast)
            {
                try
                {
                    var notifDto = new NotificationDto
                    {
                        Id = n.Id,
                        Title = n.Title,
                        Copy = n.Copy,
                        CreatedAt = new DateTimeOffset(n.CreatedAtUtc, TimeSpan.Zero),
                        Time = "Vừa xong",
                        Icon = n.Icon,
                        Read = false,
                        ActionUrl = n.ActionUrl
                    };
                    await hubContext.Clients.Group($"user-{n.UserId}").SendAsync("ReceiveNotification", notifDto);
                }
                catch
                {
                    // Non-blocking broadcast
                }
            }
        }

        return Ok(ApiResponse<EpisodeDto>.Ok(new EpisodeDto
        {
            Id = episode.Id,
            MovieId = episode.MovieId,
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            DurationMinutes = episode.DurationMinutes,
            ThumbnailUrl = episode.ThumbnailUrl,
            Overview = episode.Overview
        }, "Thêm tập phim thành công."));
    }

    [HttpDelete("episodes/{episodeId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteEpisode(int episodeId)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy tập phim."));
        }

        var movieId = episode.MovieId;
        context.Episodes.Remove(episode);

        var movie = await context.Movies.FindAsync(movieId);
        if (movie is not null)
        {
            var remaining = await context.Episodes.Where(e => e.MovieId == movieId && e.Id != episodeId).ToListAsync();
            movie.CurrentEpisode = remaining.Count > 0 ? remaining.Max(e => e.EpisodeNumber) : 0;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "DELETE_EPISODE", "Episode", episodeId.ToString(), $"Xóa tập phim ID {episodeId}.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa tập phim."));
    }

    [HttpGet("episodes/{episodeId:int}/streams")]
    public async Task<ActionResult<ApiResponse<List<StreamSourceDto>>>> GetStreamsForEpisode(int episodeId)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var streams = await context.StreamSources
            .Where(s => s.EpisodeId == episodeId)
            .OrderBy(s => s.Position)
            .Select(s => new StreamSourceDto
            {
                Id = s.Position,
                EpisodeId = s.EpisodeId,
                Position = s.Position,
                ServerName = s.ServerName,
                ServerType = s.ServerType,
                Quality = s.Quality,
                StreamUrl = s.StreamUrl
            })
            .ToListAsync();

        return Ok(ApiResponse<List<StreamSourceDto>>.Ok(streams));
    }

    [HttpPost("episodes/{episodeId:int}/streams")]
    public async Task<ActionResult<ApiResponse<StreamSourceDto>>> AddStreamSource(int episodeId, [FromBody] UpsertStreamSourceRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var ep = await context.Episodes.FindAsync(episodeId);
        if (ep is null) return NotFound(ApiResponse<StreamSourceDto>.Fail("Không tìm thấy tập phim."));

        var existing = await context.StreamSources.Where(s => s.EpisodeId == episodeId).ToListAsync();
        var nextPos = existing.Count > 0 ? existing.Max(s => s.Position) + 1 : 1;

        var source = new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = nextPos,
            ServerName = string.IsNullOrWhiteSpace(request.ServerName) ? $"Server {nextPos}" : request.ServerName.Trim(),
            ServerType = request.ServerType,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
            StreamUrl = request.StreamUrl.Trim()
        };

        context.StreamSources.Add(source);
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "ADD_STREAM", "StreamSource", $"{episodeId}-{nextPos}", $"Thêm nguồn stream '{source.ServerName}' ({source.Quality}) cho tập {episodeId}.");

        return Ok(ApiResponse<StreamSourceDto>.Ok(new StreamSourceDto
        {
            Id = source.Position,
            EpisodeId = source.EpisodeId,
            Position = source.Position,
            ServerName = source.ServerName,
            ServerType = source.ServerType,
            Quality = source.Quality,
            StreamUrl = source.StreamUrl
        }, "Đã thêm nguồn stream thành công."));
    }

    [HttpPut("episodes/{episodeId:int}/streams/{position:int}")]
    public async Task<ActionResult<ApiResponse<StreamSourceDto>>> UpdateStreamSource(int episodeId, int position, [FromBody] UpsertStreamSourceRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var stream = await context.StreamSources.FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == position);
        if (stream is null) return NotFound(ApiResponse<StreamSourceDto>.Fail("Không tìm thấy nguồn stream cần sửa."));

        stream.ServerName = request.ServerName.Trim();
        stream.ServerType = request.ServerType;
        stream.Quality = request.Quality.Trim();
        stream.StreamUrl = request.StreamUrl.Trim();

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPDATE_STREAM", "StreamSource", $"{episodeId}-{position}", $"Cập nhật nguồn stream vị trí {position} cho tập {episodeId}.");

        return Ok(ApiResponse<StreamSourceDto>.Ok(new StreamSourceDto
        {
            Id = stream.Position,
            EpisodeId = stream.EpisodeId,
            Position = stream.Position,
            ServerName = stream.ServerName,
            ServerType = stream.ServerType,
            Quality = stream.Quality,
            StreamUrl = stream.StreamUrl
        }, "Cập nhật nguồn stream thành công."));
    }

    [HttpDelete("episodes/{episodeId:int}/streams/{position:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteStreamSource(int episodeId, int position)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var stream = await context.StreamSources.FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == position);
        if (stream is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy nguồn stream để xóa."));

        context.StreamSources.Remove(stream);
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "DELETE_STREAM", "StreamSource", $"{episodeId}-{position}", $"Xóa nguồn stream vị trí {position} của tập {episodeId}.");
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa nguồn stream."));
    }

    [HttpPost("streams/probe")]
    public async Task<ActionResult<ApiResponse<ProbeStreamResultDto>>> ProbeStream([FromBody] ProbeStreamRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (isSafe, errorMessage, validatedUri) = await SafeStreamUrlValidator.ValidateUrlAsync(request.StreamUrl);
            if (!isSafe || validatedUri is null)
            {
                sw.Stop();
                return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
                {
                    IsSuccess = false,
                    StatusCode = 400,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ErrorMessage = errorMessage ?? "URL không hợp lệ hoặc bị chặn vì lý do an toàn mạng nội bộ."
                }));
            }

            using var client = HttpClientFactory?.CreateClient() ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            client.Timeout = TimeSpan.FromSeconds(5);
            using var req = new HttpRequestMessage(HttpMethod.Head, validatedUri);
            req.Headers.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");

            var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (res.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024);
                res = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
            }
            sw.Stop();

            var result = new ProbeStreamResultDto
            {
                IsSuccess = res.IsSuccessStatusCode,
                StatusCode = (int)res.StatusCode,
                LatencyMs = sw.ElapsedMilliseconds,
                ContentType = res.Content.Headers.ContentType?.MediaType ?? "video/mp4"
            };

            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            sw.Stop();
            var result = new ProbeStreamResultDto
            {
                IsSuccess = false,
                StatusCode = 0,
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(result));
        }
    }

    [HttpPost("episodes/batch-generate")]
    public async Task<ActionResult<ApiResponse<int>>> BatchGenerateEpisodes([FromBody] BatchEpisodeGenerateRequest request)
    {
        if (request.StartEpisode > request.EndEpisode)
        {
            return BadRequest(ApiResponse<int>.Fail("Tập bắt đầu không thể lớn hơn tập kết thúc."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(request.MovieId);
        if (movie is null) return NotFound(ApiResponse<int>.Fail("Không tìm thấy phim."));

        var existingEpisodes = (await context.Episodes
            .Where(e => e.MovieId == request.MovieId && e.EpisodeNumber >= request.StartEpisode && e.EpisodeNumber <= request.EndEpisode)
            .Select(e => e.EpisodeNumber)
            .ToListAsync())
            .ToHashSet();

        var createdCount = 0;
        for (var epNum = request.StartEpisode; epNum <= request.EndEpisode; epNum++)
        {
            if (existingEpisodes.Contains(epNum))
            {
                continue;
            }

            var title = request.TitlePattern.Replace("{n}", epNum.ToString());
            var streamUrl = request.StreamUrlPattern.Replace("{n}", epNum.ToString());

            var episode = new EpisodeEntity
            {
                MovieId = request.MovieId,
                EpisodeNumber = epNum,
                Title = title,
                DurationMinutes = request.DurationMinutes,
                ThumbnailUrl = movie.BackdropUrl,
                Overview = $"Diễn biến kịch tính của {title}."
            };

            episode.Streams.Add(new StreamSourceEntity
            {
                Position = 1,
                ServerName = request.ServerName,
                ServerType = request.ServerType,
                Quality = request.Quality,
                StreamUrl = streamUrl
            });

            context.Episodes.Add(episode);
            createdCount++;
        }

        if (request.EndEpisode > movie.TotalEpisodes) movie.TotalEpisodes = request.EndEpisode;
        if (request.EndEpisode > movie.CurrentEpisode) movie.CurrentEpisode = request.EndEpisode;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "BATCH_GENERATE_EPISODES", "Movie", request.MovieId.ToString(), $"Tạo tự động {createdCount} tập cho phim '{movie.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        return Ok(ApiResponse<int>.Ok(createdCount, $"Đã tạo tự động thành công {createdCount} tập phim mới."));
    }
}
