using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EpisodesController(
    IDbContextFactory<AppDbContext> contextFactory,
    IStreamUrlSigner? urlSigner = null,
    IMemoryCache? cache = null,
    PublicUrlResolver? publicUrlResolver = null) : ControllerBase
{
    private readonly IStreamUrlSigner _urlSigner = urlSigner ?? new StreamUrlSigner();
    private readonly PublicUrlResolver _publicUrl = publicUrlResolver ?? new PublicUrlResolver(new ConfigurationBuilder().Build());
    [HttpGet("movie/{movieId:int}")]
    public async Task<ActionResult<ApiResponse<List<EpisodeDto>>>> GetEpisodesByMovieId(
        int movieId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var movieExists = await db.Movies.AnyAsync(m => m.Id == movieId);
        if (!movieExists)
            return NotFound(ApiResponse<List<EpisodeDto>>.Fail($"Không tìm thấy phim với ID: {movieId}"));

        var query = db.Episodes
            .Where(e => e.MovieId == movieId)
            .OrderBy(e => e.EpisodeNumber);

        // Không cho client tải toàn bộ danh sách tập bằng pageSize=0 hoặc một giá trị khổng lồ.
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 100);

        var pagedQuery = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var episodes = await pagedQuery
            .Include(e => e.Streams)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                MovieId = e.MovieId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                ThumbnailUrl = e.ThumbnailUrl,
                Overview = e.Overview,
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

        var isVip = await IsCallerVipAsync(db);
        MaskVipStreams(episodes, isVip);
        return Ok(ApiResponse<List<EpisodeDto>>.Ok(episodes));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<EpisodeDto>>> GetEpisodeById(int id)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var episode = await db.Episodes
            .Include(e => e.Streams)
            .Where(e => e.Id == id)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                MovieId = e.MovieId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                ThumbnailUrl = e.ThumbnailUrl,
                Overview = e.Overview,
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
            .FirstOrDefaultAsync();

        if (episode == null)
            return NotFound(ApiResponse<EpisodeDto>.Fail($"Không tìm thấy tập phim với ID: {id}"));

        var isVip = await IsCallerVipAsync(db);
        MaskVipStreams([episode], isVip);
        return Ok(ApiResponse<EpisodeDto>.Ok(episode));
    }

    private async Task<bool> IsCallerVipAsync(AppDbContext db)
    {
        if (User?.Identity?.IsAuthenticated != true) return false;
        if (User.IsInRole("Admin")) return true;

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            var cacheKey = $"vip_status_{userId}";
            if (cache is not null && cache.TryGetValue(cacheKey, out bool cachedVip))
            {
                return cachedVip;
            }

            var user = await db.Users.FindAsync(userId);
            var isVip = user is not null && user.Membership != WebXemPhim.Shared.Enums.MembershipTier.Free && (!user.VipExpiresAtUtc.HasValue || user.VipExpiresAtUtc.Value > DateTime.UtcNow);
            
            if (cache is not null)
            {
                cache.Set(cacheKey, isVip, TimeSpan.FromMinutes(5));
            }
            return isVip;
        }
        else if (User.FindFirst("membership")?.Value is { } m && !m.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void MaskVipStreams(IEnumerable<EpisodeDto> episodes, bool isVip)
    {
        var baseUrl = _publicUrl.Resolve(Request);
        var clientIp = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        foreach (var ep in episodes)
        {
            // Ảnh nền tập do API sinh: đường dẫn lưu trong CSDL là tương đối, nhưng thẻ <img>
            // của trình duyệt lại phân giải theo origin của trang web, nên phải trả về URL
            // tuyệt đối trỏ tới API mà trình duyệt thực sự truy cập được.
            if (!string.IsNullOrWhiteSpace(ep.ThumbnailUrl)
                && ep.ThumbnailUrl.StartsWith("/api/episodes/", StringComparison.OrdinalIgnoreCase))
            {
                ep.ThumbnailUrl = $"{baseUrl}{ep.ThumbnailUrl}";
            }
        }

        foreach (var ep in episodes)
        {
            foreach (var s in ep.Streams)
            {
                var isVipStream = s.ServerType == WebXemPhim.Shared.Enums.StreamServerType.VipFastServer ||
                    (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));

                if (isVipStream)
                {
                    // Luồng VIP không bao giờ trả sẵn URL không gắn sid. Người dùng VIP phải gọi /api/stream/sign sau khi có lease.
                    s.StreamUrl = string.Empty;
                    continue;
                }

                // Mọi nguồn — kể cả nguồn thường — đều được ký trước khi đưa ra. Nếu chỉ ký
                // nguồn VIP thì URL CDN gốc của nguồn thường bị lộ nguyên vẹn và ai cũng tải
                // trực tiếp được, khiến cơ chế ký và giới hạn thời hạn mất tác dụng.
                s.StreamUrl = _urlSigner.GeneratePlaybackUrl(baseUrl, s.EpisodeId, s.Position, clientIp, scope: "play");
            }
        }
    }
}
