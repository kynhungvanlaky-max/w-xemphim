using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
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
    IHubContext<CinemaHub>? hubContext = null,
    EpisodeThumbnailStorage? thumbnailStorage = null,
    PublicUrlResolver? publicUrlResolver = null,
    EpisodeVideoStorage? videoStorage = null,
    WebXemPhim.Server.Services.CloudStorage.ICloudStorageService? cloudStorage = null,
    IStreamPolicyService? streamPolicyService = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet("movies/{id:int}/episodes")]
    public async Task<ActionResult<ApiResponse<List<EpisodeDto>>>> GetEpisodes(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var rawEpisodes = await context.Episodes
            .Where(e => e.MovieId == id)
            .OrderBy(e => e.EpisodeNumber)
            .Select(e => new
            {
                e.Id,
                e.MovieId,
                e.EpisodeNumber,
                e.Title,
                e.DurationMinutes,
                e.ThumbnailUrl,
                e.Overview,
                Streams = e.Streams.OrderBy(s => s.Position).Select(s => new StreamSourceDto
                {
                    Id = s.Position,
                    EpisodeId = s.EpisodeId,
                    Position = s.Position,
                    ServerName = s.ServerName,
                    ServerType = s.ServerType,
                    Quality = s.Quality,
                    StreamUrl = s.StreamUrl
                }).ToList()
            })
            .ToListAsync();

        var baseUrl = publicUrlResolver?.Resolve(Request)
            ?? (Request?.Host.HasValue == true ? $"{Request.Scheme}://{Request.Host}" : "http://localhost");

        var episodes = rawEpisodes.Select(e =>
        {
            var thumb = e.ThumbnailUrl;
            if (!string.IsNullOrWhiteSpace(thumb) && thumb.StartsWith("/api/episodes/", StringComparison.OrdinalIgnoreCase))
            {
                thumb = $"{baseUrl.TrimEnd('/')}{thumb}";
            }

            return new EpisodeDto
            {
                Id = e.Id,
                MovieId = e.MovieId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                ThumbnailUrl = thumb,
                Overview = e.Overview,
                StreamSources = e.Streams
            };
        }).ToList();

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
            ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? movie.BackdropUrl : NormalizeThumbnailUrl(request.ThumbnailUrl),
            Overview = string.IsNullOrWhiteSpace(request.Overview) ? $"Nội dung tập {episodeNumber}." : request.Overview.Trim()
        };

        var streamUrl = string.IsNullOrWhiteSpace(request.StreamUrl)
            ? "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
            : request.StreamUrl.Trim();

        var (isStreamSafe, streamError) = await ValidateStreamUrlAsync(streamUrl);
        if (!isStreamSafe)
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail(streamError ?? "Địa chỉ stream không an toàn."));
        }

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

            if (request.AutoDistributeMultiQuality)
            {
                var storage = videoStorage ?? new EpisodeVideoStorage(Env!, new ConfigurationBuilder().Build());
                var autoStreams = storage.BuildAutoDistributedStreams(
                    episode.Id,
                    streamUrl,
                    string.IsNullOrWhiteSpace(request.MasterMaxQuality) ? request.Quality : request.MasterMaxQuality,
                    isLocalUpload: streamUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase));

                foreach (var s in autoStreams)
                {
                    s.EpisodeId = episode.Id;
                    context.StreamSources.Add(s);
                }
            }
            else
            {
                context.StreamSources.Add(new StreamSourceEntity
                {
                    EpisodeId = episode.Id,
                    Position = 1,
                    ServerName = string.IsNullOrWhiteSpace(request.ServerName) ? "VIP Server" : request.ServerName.Trim(),
                    ServerType = request.ServerType,
                    Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
                    StreamUrl = streamUrl
                });
            }

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

        // Thông báo tập mới được đẩy ra nền (fire-and-forget) để phản hồi tạo tập không bị chặn.
        var notificationPayload = new
        {
            MovieId = id,
            MovieTitle = movie.Title,
            EpisodeId = episode.Id,
            EpisodeNumber = episodeNumber,
            EpisodeTitle = episode.Title,
            CreatedAt = UtcNow
        };
        _ = Task.Run(async () =>
        {
            try
            {
                await using var nctx = await ContextFactory.CreateDbContextAsync();
                var interestedUsers = await (from lib in nctx.UserLibrary
                                             where lib.MovieId == notificationPayload.MovieId
                                             join s in nctx.UserSettings on lib.UserId equals s.UserId into settingsGroup
                                             from s in settingsGroup.DefaultIfEmpty()
                                             where s == null || s.NotifyNewEpisodes
                                             select lib.UserId).Distinct().ToListAsync();
                if (interestedUsers.Count > 0)
                {
                    var notifications = interestedUsers.Select(uid => new NotificationEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = uid,
                        Title = $"Tập mới: {notificationPayload.MovieTitle}",
                        Copy = $"Tập {notificationPayload.EpisodeNumber} ({notificationPayload.EpisodeTitle}) vừa được phát hành.",
                        Icon = "play",
                        ActionUrl = $"/watch/{notificationPayload.MovieId}?ep={notificationPayload.EpisodeNumber}",
                        Read = false,
                        CreatedAtUtc = notificationPayload.CreatedAt
                    }).ToList();
                    nctx.Notifications.AddRange(notifications);
                    await nctx.SaveChangesAsync();

                    if (hubContext != null)
                    {
                        foreach (var n in notifications)
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
                            catch { /* non-blocking broadcast */ }
                        }
                    }
                }
            }
            catch
            {
                // Non-critical notification generation should not block episode creation
            }
        });

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

    [HttpPut("episodes/{episodeId:int}")]
    public async Task<ActionResult<ApiResponse<EpisodeDto>>> UpdateEpisode(int episodeId, [FromBody] UpdateEpisodeRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<EpisodeDto>.Fail("Không tìm thấy tập phim."));
        }

        var movie = await context.Movies.FindAsync(episode.MovieId);
        var oldNumber = episode.EpisodeNumber;
        var newNumber = request.EpisodeNumber;

        if (newNumber != oldNumber && await context.Episodes.AnyAsync(e => e.MovieId == episode.MovieId && e.EpisodeNumber == newNumber && e.Id != episodeId))
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail($"Tập số {newNumber} đã tồn tại trong phim này."));
        }

        episode.EpisodeNumber = newNumber;
        episode.Title = string.IsNullOrWhiteSpace(request.Title) ? $"Tập {newNumber}" : request.Title.Trim();
        episode.DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 45;
        if (!string.IsNullOrWhiteSpace(request.ThumbnailUrl))
        {
            episode.ThumbnailUrl = NormalizeThumbnailUrl(request.ThumbnailUrl);
        }
        episode.Overview = string.IsNullOrWhiteSpace(request.Overview) ? $"Nội dung tập {newNumber}." : request.Overview.Trim();

        if (movie is not null)
        {
            var maxOtherEpisode = await context.Episodes
                .Where(e => e.MovieId == episode.MovieId && e.Id != episodeId)
                .MaxAsync(e => (int?)e.EpisodeNumber) ?? 0;
            movie.CurrentEpisode = Math.Max(maxOtherEpisode, newNumber);
            if (movie.CurrentEpisode > movie.TotalEpisodes) movie.TotalEpisodes = movie.CurrentEpisode;
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPDATE_EPISODE", "Episode", episodeId.ToString(), $"Cập nhật thông tin tập {newNumber} (phim {episode.MovieId}).");

        return Ok(ApiResponse<EpisodeDto>.Ok(new EpisodeDto
        {
            Id = episode.Id,
            MovieId = episode.MovieId,
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            DurationMinutes = episode.DurationMinutes,
            ThumbnailUrl = episode.ThumbnailUrl,
            Overview = episode.Overview
        }, "Cập nhật tập phim thành công."));
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
        // Xoá file vật lý (thumbnail/video) đẩy ra nền để không chặn phản hồi xoá tập.
        _ = Task.Run(() =>
        {
            try
            {
                thumbnailStorage?.Delete(episodeId);
                videoStorage?.DeleteEpisodeVideos(episodeId);
            }
            catch
            {
                // Storage cleanup is best-effort; a failure must not fail the delete.
            }
        });

        var movie = await context.Movies.FindAsync(movieId);
        if (movie is not null)
        {
            var maxRemaining = await context.Episodes
                .Where(e => e.MovieId == movieId && e.Id != episodeId)
                .MaxAsync(e => (int?)e.EpisodeNumber) ?? 0;
            movie.CurrentEpisode = maxRemaining;
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

    [HttpPost("episodes/{episodeId:int}/thumbnail")]
    [Consumes("multipart/form-data")]
    // Kestrel cho phép tối đa 6 MB toàn cục; riêng endpoint này chỉ cần 5 MB ảnh + 1 MB
    // multipart overhead. Giới hạn tại action ngăn request lớn hơn bị đọc vào bộ nhớ.
    [RequestSizeLimit(EpisodeThumbnailStorage.MaxFileSizeBytes + 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<EpisodeDto>>> UploadEpisodeThumbnail(int episodeId, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail("Vui lòng chọn một tệp ảnh hợp lệ."));
        }

        if (file.Length > EpisodeThumbnailStorage.MaxFileSizeBytes)
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail("Dung lượng ảnh vượt quá giới hạn 5 MB."));
        }

        var extension = EpisodeThumbnailStorage.ResolveExtension(file.FileName);
        if (extension is null)
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail("Định dạng tệp không được hỗ trợ. Chỉ chấp nhận ảnh JPG, PNG, WEBP, GIF, AVIF."));
        }

        await using var stream = file.OpenReadStream();
        var header = await EpisodeThumbnailStorage.ReadHeaderAsync(stream);
        if (!EpisodeThumbnailStorage.HasValidSignature(header, extension))
        {
            return BadRequest(ApiResponse<EpisodeDto>.Fail("Tệp tải lên không phải là ảnh hợp lệ hoặc đã bị thay đổi định dạng."));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<EpisodeDto>.Fail("Không tìm thấy tập phim."));
        }

        var storage = thumbnailStorage ?? new EpisodeThumbnailStorage(Env!, new ConfigurationBuilder().Build());
        await storage.SaveAsync(episodeId, extension, stream);

        var version = TimeProvider.GetUtcNow().ToUnixTimeSeconds();
        episode.ThumbnailUrl = EpisodeThumbnailUrls.Custom(episodeId, version);
        await context.SaveChangesAsync();

        await LogAuditAsync(context, "UPLOAD_EPISODE_THUMBNAIL", "Episode", episodeId.ToString(), $"Tải lên ảnh nền cho tập {episode.EpisodeNumber} (phim {episode.MovieId}).");

        var baseUrl = publicUrlResolver?.Resolve(Request)
            ?? (Request?.Host.HasValue == true ? $"{Request.Scheme}://{Request.Host}" : "http://localhost");
        var publicThumbUrl = $"{baseUrl.TrimEnd('/')}{episode.ThumbnailUrl}";

        return Ok(ApiResponse<EpisodeDto>.Ok(new EpisodeDto
        {
            Id = episode.Id,
            MovieId = episode.MovieId,
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            DurationMinutes = episode.DurationMinutes,
            ThumbnailUrl = publicThumbUrl,
            Overview = episode.Overview
        }, "Tải lên ảnh nền tập phim thành công."));
    }

    [HttpDelete("episodes/{episodeId:int}/thumbnail")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteEpisodeThumbnail(int episodeId)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy tập phim."));
        }

        thumbnailStorage?.Delete(episodeId);

        episode.ThumbnailUrl = EpisodeThumbnailUrls.Generated(episode.MovieId, episode.EpisodeNumber);
        await context.SaveChangesAsync();

        await LogAuditAsync(context, "DELETE_EPISODE_THUMBNAIL", "Episode", episodeId.ToString(), $"Khôi phục ảnh nền mặc định cho tập {episode.EpisodeNumber} (phim {episode.MovieId}).");

        return Ok(ApiResponse<bool>.Ok(true, "Đã khôi phục ảnh nền mặc định."));
    }

    [HttpPost("episodes/{episodeId:int}/video")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(EpisodeVideoStorage.DefaultMaxFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = EpisodeVideoStorage.DefaultMaxFileSizeBytes)]
    public async Task<ActionResult<ApiResponse<VideoIngestResultDto>>> UploadEpisodeVideo(
        int episodeId,
        IFormFile? file,
        [FromForm] string? masterMaxQuality = "1080p FHD",
        [FromForm] bool replaceExisting = true,
        [FromForm] string? targetServers = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<VideoIngestResultDto>.Fail("Vui lòng chọn một tệp video hợp lệ."));
        }

        var storage = videoStorage ?? new EpisodeVideoStorage(Env!, new ConfigurationBuilder().Build());
        if (file.Length > storage.MaxFileSizeBytes)
        {
            return BadRequest(ApiResponse<VideoIngestResultDto>.Fail($"Dung lượng video vượt quá giới hạn {storage.MaxFileSizeBytes / (1024 * 1024)} MB."));
        }

        var extension = EpisodeVideoStorage.ResolveExtension(file.FileName);
        if (extension is null)
        {
            return BadRequest(ApiResponse<VideoIngestResultDto>.Fail("Định dạng tệp không được hỗ trợ. Chỉ chấp nhận video MP4, WEBM, MKV, MOV."));
        }

        await using var stream = file.OpenReadStream();
        var header = await EpisodeVideoStorage.ReadHeaderAsync(stream);
        if (!EpisodeVideoStorage.HasValidSignature(header, extension))
        {
            return BadRequest(ApiResponse<VideoIngestResultDto>.Fail("Tệp tải lên không phải là video hợp lệ hoặc đã bị thay đổi phần mở rộng."));
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<VideoIngestResultDto>.Fail("Không tìm thấy tập phim."));
        }

        var maxQuality = string.IsNullOrWhiteSpace(masterMaxQuality) ? "1080p FHD" : masterMaxQuality.Trim();
        var createdFiles = await storage.SaveMasterAndRenditionsAsync(episodeId, extension, stream, maxQuality);

        var storageMode = "Local Disk";
        var masterStreamUrl = $"local://ep-{episodeId}-master{extension}";
        var renditionUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Tải dần từng bản chất lượng lên Cloudflare R2 (nếu R2 đã cấu hình)
        if (cloudStorage?.IsConfigured == true)
        {
            try
            {
                foreach (var fileName in createdFiles)
                {
                    var localPath = Path.Combine(storage.RootPath, fileName);
                    if (System.IO.File.Exists(localPath))
                    {
                        await using var fs = System.IO.File.OpenRead(localPath);
                        var r2Key = $"episodes/{episodeId}/{fileName}";
                        var contentType = EpisodeVideoStorage.ResolveContentType(extension) ?? "video/mp4";
                        var uploadedUrl = await cloudStorage.UploadFileAsync(fs, r2Key, contentType);
                        if (!string.IsNullOrWhiteSpace(uploadedUrl))
                        {
                            var slug = EpisodeVideoStorage.ExtractSlugFromFileName(fileName, episodeId);
                            renditionUrls[slug] = uploadedUrl;
                            storageMode = "Cloudflare R2";
                            if (slug == "master") masterStreamUrl = uploadedUrl;
                        }
                    }
                }
            }
            catch
            {
                storageMode = "Local Disk (Cloud fallback)";
            }
        }

        // Với các file chưa có link R2, fallback về local
        foreach (var fileName in createdFiles)
        {
            var slug = EpisodeVideoStorage.ExtractSlugFromFileName(fileName, episodeId);
            if (!renditionUrls.ContainsKey(slug))
            {
                renditionUrls[slug] = $"local://{fileName}";
            }
        }

        List<string>? serverFilter = null;
        if (!string.IsNullOrWhiteSpace(targetServers) && !targetServers.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            serverFilter = targetServers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        var autoStreams = storage.BuildAutoDistributedStreams(
            episodeId,
            masterStreamUrl,
            maxQuality,
            isLocalUpload: storageMode.StartsWith("Local", StringComparison.OrdinalIgnoreCase),
            targetServerNames: serverFilter,
            policyService: streamPolicyService,
            renditionUrlMap: renditionUrls);

        if (replaceExisting)
        {
            var oldStreams = await context.StreamSources.Where(s => s.EpisodeId == episodeId).ToListAsync();
            if (oldStreams.Count > 0)
            {
                context.StreamSources.RemoveRange(oldStreams);
                await context.SaveChangesAsync();
            }
        }

        var startingPos = replaceExisting ? 1 : ((await context.StreamSources.Where(s => s.EpisodeId == episodeId).MaxAsync(s => (int?)s.Position) ?? 0) + 1);
        foreach (var s in autoStreams)
        {
            s.Position = startingPos++;
            context.StreamSources.Add(s);
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPLOAD_EPISODE_VIDEO", "Episode", episodeId.ToString(),
            $"Tải lên video master ({file.FileName}, {file.Length / (1024 * 1024)} MB) và tự động tạo {autoStreams.Count} luồng chất lượng ({maxQuality}) cho tập {episode.EpisodeNumber}.");

        var streamDtos = autoStreams.Select(s => new StreamSourceDto
        {
            Id = s.Position,
            EpisodeId = s.EpisodeId,
            Position = s.Position,
            ServerName = s.ServerName,
            ServerType = s.ServerType,
            Quality = s.Quality,
            StreamUrl = s.StreamUrl
        }).ToList();

        var resultDto = new VideoIngestResultDto
        {
            EpisodeId = episodeId,
            StorageMode = storageMode,
            MasterFileName = file.FileName,
            FileSizeBytes = file.Length,
            GeneratedQualities = EpisodeVideoStorage.GetQualityLadder(maxQuality),
            DistributedServersCount = autoStreams.Select(s => s.ServerName).Distinct().Count(),
            Streams = streamDtos
        };

        return Ok(ApiResponse<VideoIngestResultDto>.Ok(resultDto, $"Đã tải lên video và tự động phân phối {autoStreams.Count} luồng phát thành công!"));
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

        var (isStreamSafe, streamError) = await ValidateStreamUrlAsync(request.StreamUrl);
        if (!isStreamSafe)
        {
            return BadRequest(ApiResponse<StreamSourceDto>.Fail(streamError ?? "Địa chỉ stream không an toàn."));
        }

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

        var (isStreamSafe, streamError) = await ValidateStreamUrlAsync(request.StreamUrl);
        if (!isStreamSafe)
        {
            return BadRequest(ApiResponse<StreamSourceDto>.Fail(streamError ?? "Địa chỉ stream không an toàn."));
        }

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

    [HttpPost("episodes/{episodeId:int}/streams/auto-distribute")]
    public async Task<ActionResult<ApiResponse<List<StreamSourceDto>>>> AutoDistributeStreams(
        int episodeId,
        [FromBody] AutoProvisionStreamsRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<List<StreamSourceDto>>.Fail("Không tìm thấy tập phim."));
        }

        var masterUrl = request.MasterStreamUrl?.Trim();
        if (string.IsNullOrWhiteSpace(masterUrl))
        {
            var firstStream = await context.StreamSources
                .Where(s => s.EpisodeId == episodeId)
                .OrderBy(s => s.Position)
                .FirstOrDefaultAsync();

            masterUrl = firstStream?.StreamUrl;
        }

        if (string.IsNullOrWhiteSpace(masterUrl))
        {
            masterUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4";
        }

        var (isStreamSafe, streamError) = await ValidateStreamUrlAsync(masterUrl);
        if (!isStreamSafe)
        {
            return BadRequest(ApiResponse<List<StreamSourceDto>>.Fail(streamError ?? "Địa chỉ URL nguồn không an toàn."));
        }

        var storage = videoStorage ?? new EpisodeVideoStorage(Env!, new ConfigurationBuilder().Build());
        var maxQuality = string.IsNullOrWhiteSpace(request.MasterMaxQuality) ? "1080p FHD" : request.MasterMaxQuality.Trim();
        var isLocal = masterUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase);

        List<string>? serverFilter = null;
        if (!string.IsNullOrWhiteSpace(request.TargetServers) && !request.TargetServers.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            serverFilter = request.TargetServers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        var autoStreams = storage.BuildAutoDistributedStreams(
            episodeId,
            masterUrl,
            maxQuality,
            isLocal,
            targetServerNames: serverFilter,
            policyService: streamPolicyService);

        if (request.ReplaceExisting)
        {
            var oldStreams = await context.StreamSources.Where(s => s.EpisodeId == episodeId).ToListAsync();
            if (oldStreams.Count > 0)
            {
                context.StreamSources.RemoveRange(oldStreams);
                await context.SaveChangesAsync();
            }
        }

        var startingPos = request.ReplaceExisting ? 1 : ((await context.StreamSources.Where(s => s.EpisodeId == episodeId).MaxAsync(s => (int?)s.Position) ?? 0) + 1);
        foreach (var s in autoStreams)
        {
            s.Position = startingPos++;
            context.StreamSources.Add(s);
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "AUTO_DISTRIBUTE_STREAMS", "Episode", episodeId.ToString(),
            $"Tự động nhân bản và phân phối {autoStreams.Count} luồng phát ({maxQuality}) từ nguồn '{masterUrl}' cho tập {episode.EpisodeNumber}.");

        var result = await context.StreamSources
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

        return Ok(ApiResponse<List<StreamSourceDto>>.Ok(result, $"Đã tạo và phân phối {autoStreams.Count} luồng phát thành công!"));
    }

    [HttpPost("streams/probe")]
    public async Task<ActionResult<ApiResponse<ProbeStreamResultDto>>> ProbeStream([FromBody] ProbeStreamRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rawUrl = request.StreamUrl?.Trim();

        if (rawUrl?.StartsWith("local://", StringComparison.OrdinalIgnoreCase) == true)
        {
            sw.Stop();
            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
            {
                IsSuccess = true,
                StatusCode = 200,
                LatencyMs = 1,
                ContentType = "video/mp4"
            }));
        }

        var urlToProbe = rawUrl;
        var isCloudStorage = false;

        // Xử lý luồng Cloudflare R2 / S3 (r2://..., s3://... hoặc URL CDN R2)
        if (cloudStorage?.IsCloudStorageUrl(rawUrl) == true)
        {
            if (!cloudStorage.IsConfigured)
            {
                sw.Stop();
                return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
                {
                    IsSuccess = false,
                    StatusCode = 503,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ErrorMessage = "Dịch vụ Cloudflare R2 chưa được cấu hình khóa truy cập (Access Key / Secret Key)."
                }));
            }

            try
            {
                var resolvedUrl = await cloudStorage.ResolvePlaybackUrlAsync(
                    rawUrl!,
                    expiry: TimeSpan.FromMinutes(10),
                    httpMethod: "GET");

                if (string.IsNullOrWhiteSpace(resolvedUrl))
                {
                    sw.Stop();
                    return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
                    {
                        IsSuccess = false,
                        StatusCode = 404,
                        LatencyMs = sw.ElapsedMilliseconds,
                        ErrorMessage = "Không thể tạo URL kết nối đối tượng từ Cloudflare R2. Vui lòng kiểm tra lại Object Key hoặc Bucket."
                    }));
                }

                urlToProbe = resolvedUrl;
                isCloudStorage = true;
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
                {
                    IsSuccess = false,
                    StatusCode = 500,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ErrorMessage = $"Lỗi khi khởi tạo kết nối Cloudflare R2: {ex.Message}"
                }));
            }
        }
        else if (rawUrl?.StartsWith("r2://", StringComparison.OrdinalIgnoreCase) == true ||
                 rawUrl?.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) == true)
        {
            sw.Stop();
            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
            {
                IsSuccess = false,
                StatusCode = 503,
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorMessage = "Dịch vụ Cloudflare R2 chưa được khởi tạo trên máy chủ."
            }));
        }

        try
        {
            var (isSafe, errorMessage, validatedUri) = await SafeStreamUrlValidator.ValidateUrlAsync(
                urlToProbe,
                resolveDns: ShouldResolveStreamDns);

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

            using var client = HttpClientFactory?.CreateClient("StreamProbe")
                ?? new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ConnectCallback = SafeStreamUrlValidator.ConnectSafeAsync
                });
            client.Timeout = TimeSpan.FromSeconds(5);

            HttpResponseMessage res;
            if (isCloudStorage)
            {
                // URL Pre-signed của Cloudflare R2 ký theo verb GET và từ chối HEAD với mã 403.
                // Thăm dò bằng GET kèm Range: bytes=0-0 để đo độ trễ thật tới Edge của R2 mà không tốn băng thông tải file video.
                using var getReq = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                getReq.Headers.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");
                getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                res = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
            }
            else
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, validatedUri);
                req.Headers.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");
                res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                // Fallback nếu CDN/Server chặn method HEAD (403 Forbidden hoặc 405 Method Not Allowed)
                if (res.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                    res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    using var getReq = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                    getReq.Headers.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");
                    getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    res = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
                }
            }

            sw.Stop();

            var contentType = res.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = isCloudStorage ? "video/mp4" : "application/octet-stream";
            }

            var isSuccess = res.IsSuccessStatusCode;
            string? friendlyError = null;
            if (!isSuccess)
            {
                friendlyError = res.StatusCode switch
                {
                    System.Net.HttpStatusCode.NotFound => isCloudStorage
                        ? "Tệp không tồn tại trên Cloudflare R2 (HTTP 404 Not Found)."
                        : "Nguồn phát không tồn tại (HTTP 404 Not Found).",
                    System.Net.HttpStatusCode.Forbidden => isCloudStorage
                        ? "Không có quyền truy cập bucket Cloudflare R2 (HTTP 403 Forbidden)."
                        : "Truy cập bị từ chối (HTTP 403 Forbidden).",
                    _ => $"Máy chủ phản hồi mã lỗi HTTP {(int)res.StatusCode}"
                };
            }

            var result = new ProbeStreamResultDto
            {
                IsSuccess = isSuccess,
                StatusCode = (int)res.StatusCode,
                LatencyMs = sw.ElapsedMilliseconds,
                ContentType = contentType,
                ErrorMessage = friendlyError
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

    [HttpGet("streams/policies")]
    public async Task<ActionResult<ApiResponse<List<StreamServerPolicyDto>>>> GetStreamServerPolicies()
    {
        if (streamPolicyService is null)
        {
            return Ok(ApiResponse<List<StreamServerPolicyDto>>.Ok([]));
        }
        var policies = await streamPolicyService.GetAllPoliciesAsync();
        return Ok(ApiResponse<List<StreamServerPolicyDto>>.Ok(policies));
    }

    [HttpPut("streams/policies/{serverKey}")]
    public async Task<ActionResult<ApiResponse<StreamServerPolicyDto>>> UpdateStreamServerPolicy(
        string serverKey,
        [FromBody] UpdateStreamServerPolicyRequest request)
    {
        if (streamPolicyService is null)
        {
            return BadRequest(ApiResponse<StreamServerPolicyDto>.Fail("Dịch vụ điều khiển luồng phát chưa sẵn sàng."));
        }
        var updated = await streamPolicyService.UpdatePolicyAsync(serverKey, request);
        if (updated is null)
        {
            return NotFound(ApiResponse<StreamServerPolicyDto>.Fail($"Không tìm thấy máy chủ với mã: {serverKey}"));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        await LogAuditAsync(context, "UPDATE_STREAM_POLICY", "StreamServerPolicy", serverKey,
            $"Cập nhật chính sách máy chủ {updated.DisplayName}: MaxBandwidth={updated.MaxBandwidthKbps}Kbps, MaxQuality={updated.MaxQuality}, VipOnly={updated.IsVipOnly}, Active={updated.IsActive}.");

        return Ok(ApiResponse<StreamServerPolicyDto>.Ok(updated, "Cập nhật chính sách luồng phát thành công."));
    }

    [HttpPost("streams/policies/reset")]
    public async Task<ActionResult<ApiResponse<List<StreamServerPolicyDto>>>> ResetStreamServerPolicies()
    {
        if (streamPolicyService is null)
        {
            return BadRequest(ApiResponse<List<StreamServerPolicyDto>>.Fail("Dịch vụ điều khiển luồng phát chưa sẵn sàng."));
        }
        var resetList = await streamPolicyService.ResetToDefaultsAsync();

        await using var context = await ContextFactory.CreateDbContextAsync();
        await LogAuditAsync(context, "RESET_STREAM_POLICIES", "StreamServerPolicy", "all",
            "Khôi phục toàn bộ chính sách máy chủ luồng phát về mặc định ban đầu.");

        return Ok(ApiResponse<List<StreamServerPolicyDto>>.Ok(resetList, "Đã khôi phục chính sách mặc định."));
    }

    [HttpPost("episodes/batch-generate")]
    public async Task<ActionResult<ApiResponse<int>>> BatchGenerateEpisodes([FromBody] BatchEpisodeGenerateRequest request)
    {
        if (request.StartEpisode <= 0 || request.EndEpisode <= 0 || request.StartEpisode > request.EndEpisode)
        {
            return BadRequest(ApiResponse<int>.Fail("Tập bắt đầu và tập kết thúc không hợp lệ."));
        }

        if (request.EndEpisode - request.StartEpisode + 1 > 200)
        {
            return BadRequest(ApiResponse<int>.Fail("Số lượng tập sinh tự động mỗi lần không được vượt quá 200 tập."));
        }

        if (string.IsNullOrWhiteSpace(request.StreamUrlPattern))
        {
            return BadRequest(ApiResponse<int>.Fail("Mẫu URL stream không được để trống."));
        }

        var pattern = request.StreamUrlPattern.Trim();
        var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            var hostPart = pattern[(schemeEnd + 3)..];
            var pathStart = hostPart.IndexOfAny(['/', '?', '#']);
            var authority = pathStart >= 0 ? hostPart[..pathStart] : hostPart;
            if (authority.Contains("{n}", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<int>.Fail("Ký tự mẫu '{n}' chỉ được phép đặt trong đường dẫn (path) hoặc tham số (query), không được đặt trong tên miền máy chủ (host)."));
            }
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(request.MovieId);
        if (movie is null) return NotFound(ApiResponse<int>.Fail("Không tìm thấy phim."));

        var sampleStreamUrl = pattern.Replace("{n}", request.StartEpisode.ToString());
        var (isStreamSafe, streamError) = await ValidateStreamUrlAsync(sampleStreamUrl);
        if (!isStreamSafe)
        {
            return BadRequest(ApiResponse<int>.Fail(streamError ?? "Mẫu URL stream không an toàn."));
        }

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

            if (request.AutoDistributeMultiQuality)
            {
                var storage = videoStorage ?? new EpisodeVideoStorage(Env!, new ConfigurationBuilder().Build());
                var autoStreams = storage.BuildAutoDistributedStreams(
                    episode.Id,
                    streamUrl,
                    string.IsNullOrWhiteSpace(request.MasterMaxQuality) ? request.Quality : request.MasterMaxQuality,
                    isLocalUpload: streamUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase));

                foreach (var s in autoStreams)
                {
                    episode.Streams.Add(s);
                }
            }
            else
            {
                episode.Streams.Add(new StreamSourceEntity
                {
                    Position = 1,
                    ServerName = request.ServerName,
                    ServerType = request.ServerType,
                    Quality = request.Quality,
                    StreamUrl = streamUrl
                });
            }

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

    /// <summary>
    /// Chỉ phân giải DNS khi controller chạy trong host thật. Các unit test dựng controller
    /// trực tiếp không có <see cref="IWebHostEnvironment"/> nên bỏ qua bước DNS để không phụ
    /// thuộc mạng; IP literal và host bị cấm vẫn được kiểm tra.
    /// </summary>
    private bool ShouldResolveStreamDns => Env is not null && !Env.IsEnvironment("Testing");

    private async Task<(bool IsSafe, string? ErrorMessage)> ValidateStreamUrlAsync(
        string? streamUrl,
        CancellationToken cancellationToken = default)
    {
        if (streamUrl?.StartsWith("local://", StringComparison.OrdinalIgnoreCase) == true ||
            streamUrl?.StartsWith("r2://", StringComparison.OrdinalIgnoreCase) == true ||
            streamUrl?.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (true, null);
        }

        var result = await SafeStreamUrlValidator.ValidateUrlAsync(
            streamUrl,
            cancellationToken,
            ShouldResolveStreamDns);
        return (result.IsSafe, result.ErrorMessage);
    }

    private static string NormalizeThumbnailUrl(string thumbnailUrl)
    {
        var trimmed = thumbnailUrl.Trim();
        var apiIdx = trimmed.IndexOf("/api/episodes/", StringComparison.OrdinalIgnoreCase);
        if (apiIdx >= 0)
        {
            return trimmed[apiIdx..];
        }
        return trimmed;
    }
}
