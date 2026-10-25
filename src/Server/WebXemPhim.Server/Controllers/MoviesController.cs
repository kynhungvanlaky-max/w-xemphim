using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MoviesController(
    IMovieService movieService,
    IDbContextFactory<AppDbContext>? contextFactory = null,
    IStreamUrlSigner? urlSigner = null,
    IMemoryCache? memoryCache = null,
    PublicUrlResolver? publicUrlResolver = null) : ControllerBase
{
    private readonly IStreamUrlSigner _urlSigner = urlSigner ?? new StreamUrlSigner();
    private readonly PublicUrlResolver _publicUrl = publicUrlResolver ?? new PublicUrlResolver(new ConfigurationBuilder().Build());
    private readonly IMemoryCache? _memoryCache = memoryCache;
    [HttpGet("filter-options")]
    [OutputCache(Duration = 120, Tags = ["movies", "genres"])]
    public async Task<ActionResult<ApiResponse<MovieFilterOptionsDto>>> FilterOptions() =>
        Ok(ApiResponse<MovieFilterOptionsDto>.Ok(await movieService.GetFilterOptionsAsync()));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MovieDto>>>> Filter([FromQuery] MovieFilterRequest request)
    {
        var result = await movieService.FilterMoviesAsync(request);
        return Ok(ApiResponse<PagedResult<MovieDto>>.Ok(result));
    }

    [HttpGet("featured")]
    [OutputCache(Duration = 60, Tags = ["movies"])]
    public async Task<ActionResult<ApiResponse<MovieDto>>> GetFeatured()
    {
        var movie = await movieService.GetFeaturedMovieAsync();
        if (movie == null)
            return NotFound(ApiResponse<MovieDto>.Fail("Không tìm thấy phim nổi bật."));

        return Ok(ApiResponse<MovieDto>.Ok(movie));
    }

    [HttpGet("trending")]
    [OutputCache(Duration = 60, Tags = ["movies"])]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetTrending([FromQuery] int count = 10)
    {
        var movies = await movieService.GetTrendingMoviesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(movies));
    }

    [HttpGet("top-series")]
    [OutputCache(Duration = 60, Tags = ["movies"])]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetTopSeries([FromQuery] int count = 10)
    {
        var series = await movieService.GetTopSeriesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(series));
    }

    [HttpGet("latest")]
    [OutputCache(Duration = 60, Tags = ["movies"])]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetLatest([FromQuery] int count = 12)
    {
        var movies = await movieService.GetLatestMoviesAsync(count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(movies));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> GetById(int id)
    {
        var movie = await movieService.GetMovieByIdAsync(id);
        if (movie == null)
            return NotFound(ApiResponse<MovieDetailDto>.Fail($"Không tìm thấy phim với ID: {id}"));

        var isVip = await IsCallerVipAsync();
        MaskVipStreams(movie, isVip);
        return Ok(ApiResponse<MovieDetailDto>.Ok(movie));
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> GetBySlug(string slug)
    {
        var movie = await movieService.GetMovieBySlugAsync(slug);
        if (movie == null)
            return NotFound(ApiResponse<MovieDetailDto>.Fail($"Không tìm thấy phim với slug: {slug}"));

        var isVip = await IsCallerVipAsync();
        MaskVipStreams(movie, isVip);
        return Ok(ApiResponse<MovieDetailDto>.Ok(movie));
    }

    [HttpPost("{id:int}/view")]
    public async Task<ActionResult<ApiResponse<bool>>> IncrementView(int id)
    {
        var clientIp = HttpContext?.Connection?.RemoteIpAddress?.ToString();
        var success = await movieService.IncrementViewCountAsync(id, clientIp);
        if (!success)
        {
            return NotFound(ApiResponse<bool>.Fail($"Không tìm thấy phim với ID: {id}"));
        }
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("{id:int}/similar")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> GetSimilar(int id, [FromQuery] int count = 6)
    {
        var similar = await movieService.GetSimilarMoviesAsync(id, count);
        return Ok(ApiResponse<List<MovieDto>>.Ok(similar));
    }

    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<MovieDto>>>> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        var movies = await movieService.SearchMoviesAsync(q, limit);
        return Ok(ApiResponse<List<MovieDto>>.Ok(movies));
    }

    private async Task<bool> IsCallerVipAsync()
    {
        if (User?.Identity?.IsAuthenticated != true) return false;
        if (User.IsInRole("Admin")) return true;

        var uidClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var cacheKey = _memoryCache is null ? null : (Guid.TryParse(uidClaim, out var uId) ? $"vip_status_{uId}" : $"vip_status_{User.Identity?.Name}");
        if (cacheKey is not null && _memoryCache!.TryGetValue(cacheKey, out bool cached))
        {
            return cached;
        }

        var isVip = false;
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId) && contextFactory is not null)
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(userId);
            if (user is not null && user.Membership != MembershipTier.Free && (!user.VipExpiresAtUtc.HasValue || user.VipExpiresAtUtc.Value > DateTime.UtcNow))
            {
                isVip = true;
            }
        }
        else if (User.FindFirst("membership")?.Value is { } m && !m.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            isVip = true;
        }

        if (cacheKey is not null)
        {
            _memoryCache!.Set(cacheKey, isVip, TimeSpan.FromSeconds(30));
        }

        return isVip;
    }

    private void MaskVipStreams(MovieDetailDto? movie, bool isVip)
    {
        if (movie?.Episodes is null) return;
        var baseUrl = _publicUrl.Resolve(Request);
        var clientIp = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        foreach (var ep in movie.Episodes)
        {
            // Ảnh nền tập do API sinh: đường dẫn lưu trong CSDL là tương đối, nhưng thẻ <img>
            // của trình duyệt lại phân giải theo origin của trang web, nên phải trả về URL
            // tuyệt đối trỏ tới API mà trình duyệt thực sự truy cập được.
            if (!string.IsNullOrWhiteSpace(ep.ThumbnailUrl)
                && ep.ThumbnailUrl.StartsWith("/api/episodes/", StringComparison.OrdinalIgnoreCase))
            {
                ep.ThumbnailUrl = $"{baseUrl}{ep.ThumbnailUrl}";
            }

            foreach (var s in ep.Streams)
            {
                var isVipStream = s.ServerType == StreamServerType.VipFastServer ||
                    (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));

                if (isVipStream)
                {
                    // Luồng VIP không bao giờ trả sẵn URL không gắn sid. Người dùng VIP phải gọi /api/stream/sign sau khi có lease.
                    s.StreamUrl = string.Empty;
                    continue;
                }

                // Mọi nguồn — kể cả nguồn thường — đều được ký trước khi đưa ra, nếu không
                // URL CDN gốc của nguồn thường sẽ bị lộ nguyên vẹn.
                s.StreamUrl = _urlSigner.GeneratePlaybackUrl(baseUrl, s.EpisodeId, s.Position, clientIp, scope: "play");
            }
        }
    }
}
