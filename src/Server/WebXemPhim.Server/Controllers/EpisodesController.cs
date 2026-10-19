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
    IStreamUrlSigner? urlSigner = null) : ControllerBase
{
    private readonly IStreamUrlSigner _urlSigner = urlSigner ?? new StreamUrlSigner();
    [HttpGet("movie/{movieId:int}")]
    public async Task<ActionResult<ApiResponse<List<EpisodeDto>>>> GetEpisodesByMovieId(int movieId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var movieExists = await db.Movies.AnyAsync(m => m.Id == movieId);
        if (!movieExists)
            return NotFound(ApiResponse<List<EpisodeDto>>.Fail($"Không tìm thấy phim với ID: {movieId}"));

        var episodes = await db.Episodes
            .Where(e => e.MovieId == movieId)
            .OrderBy(e => e.EpisodeNumber)
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
            var user = await db.Users.FindAsync(userId);
            if (user is not null && user.Membership != WebXemPhim.Shared.Enums.MembershipTier.Free && (!user.VipExpiresAtUtc.HasValue || user.VipExpiresAtUtc.Value > DateTime.UtcNow))
            {
                return true;
            }
        }
        else if (User.FindFirst("membership")?.Value is { } m && !m.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void MaskVipStreams(IEnumerable<EpisodeDto> episodes, bool isVip)
    {
        var baseUrl = Request?.Host.HasValue == true
            ? $"{Request.Scheme}://{Request.Host}"
            : "http://localhost";
        var clientIp = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        foreach (var ep in episodes)
        {
            foreach (var s in ep.Streams)
            {
                var isVipStream = s.ServerType == WebXemPhim.Shared.Enums.StreamServerType.VipFastServer ||
                    (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));

                if (isVipStream)
                {
                    if (isVip)
                    {
                        s.StreamUrl = _urlSigner.GeneratePlaybackUrl(baseUrl, s.EpisodeId, s.Position, clientIp);
                    }
                    else
                    {
                        s.StreamUrl = string.Empty;
                    }
                }
            }
        }
    }
}
