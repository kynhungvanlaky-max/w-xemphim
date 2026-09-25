using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/library")]
[Route("api/watchlist")]
public sealed class LibraryController(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<WatchlistItemDto>>>> Get()
    {
        await using var db = await factory.CreateDbContextAsync();
        var entries = await (from saved in db.UserLibrary.AsNoTracking()
                             join movie in db.Movies on saved.MovieId equals movie.Id
                             where saved.UserId == UserId
                             orderby saved.AddedAt descending
                             select new WatchlistItemDto
                             {
                                 MovieId = movie.Id, MovieTitle = movie.Title, PosterUrl = movie.PosterUrl,
                                 Quality = movie.Quality, Rating = movie.Rating, ReleaseYear = movie.ReleaseYear,
                                 AddedAt = saved.AddedAt
                             }).ToListAsync();
        return Ok(ApiResponse<List<WatchlistItemDto>>.Ok(entries));
    }

    [HttpGet("check/{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> Check(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return Ok(ApiResponse<bool>.Ok(await db.UserLibrary.AnyAsync(x => x.UserId == UserId && x.MovieId == movieId)));
    }

    [HttpPost("{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> Add(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Movies.AnyAsync(x => x.Id == movieId)) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));
        if (!await db.UserLibrary.AnyAsync(x => x.UserId == UserId && x.MovieId == movieId))
        {
            db.UserLibrary.Add(new() { UserId = UserId, MovieId = movieId, AddedAt = clock.GetUtcNow().UtcDateTime });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Concurrent saves are idempotent, but unrelated database errors must surface.
                if (!await db.UserLibrary.AsNoTracking().AnyAsync(x => x.UserId == UserId && x.MovieId == movieId)) throw;
            }
        }
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> Remove(int movieId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.UserLibrary.FindAsync(UserId, movieId);
        if (entry is null) return Ok(ApiResponse<bool>.Ok(false, "Phim không tồn tại trong tủ phim."));
        db.UserLibrary.Remove(entry);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa phim khỏi tủ phim."));
    }

    [HttpGet("continue-watching")]
    public async Task<ActionResult<ApiResponse<List<WatchProgressDto>>>> GetContinueWatching()
    {
        await using var db = await factory.CreateDbContextAsync();
        var progressList = await db.WatchProgress
            .AsNoTracking()
            .Where(p => p.UserId == UserId)
            .Include(p => p.Movie)
            .OrderByDescending(p => p.LastWatchedAtUtc)
            .Take(10)
            .ToListAsync();

        var result = progressList.Select(p =>
        {
            var totalSec = p.DurationSeconds > 0 ? p.DurationSeconds : (p.Movie.DurationMinutes * 60);
            if (totalSec <= 0) totalSec = 120 * 60;
            var pct = Math.Clamp((int)Math.Round((double)p.ProgressSeconds / totalSec * 100), 1, 99);
            var remainingSec = Math.Max(0, totalSec - p.ProgressSeconds);
            var remainingMin = Math.Max(1, (int)Math.Ceiling(remainingSec / 60.0));

            return new WatchProgressDto
            {
                MovieId = p.MovieId,
                MovieTitle = p.Movie.Title,
                PosterUrl = p.Movie.PosterUrl,
                BackdropUrl = p.Movie.BackdropUrl,
                EpisodeNumber = p.EpisodeNumber,
                EpisodeTitle = p.EpisodeNumber > 1 ? $"Tập {p.EpisodeNumber:00}" : (p.Movie.Type == Shared.Enums.MovieType.Series ? $"Tập {p.EpisodeNumber:00}" : "Bản đầy đủ"),
                DurationMinutes = p.Movie.DurationMinutes,
                ProgressSeconds = p.ProgressSeconds,
                DurationSeconds = totalSec,
                ProgressPercent = pct,
                RemainingMinutes = remainingMin,
                LastWatchedAt = new DateTimeOffset(p.LastWatchedAtUtc, TimeSpan.Zero)
            };
        }).ToList();

        return Ok(ApiResponse<List<WatchProgressDto>>.Ok(result));
    }

    [HttpPost("progress")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateProgress([FromBody] WebXemPhim.Shared.Requests.UpdateWatchProgressRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();
        var movie = await db.Movies.FindAsync(request.MovieId);
        if (movie is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));

        var item = await db.WatchProgress.FirstOrDefaultAsync(p => p.UserId == UserId && p.MovieId == request.MovieId);
        var now = clock.GetUtcNow().UtcDateTime;

        if (item is null)
        {
            db.WatchProgress.Add(new WatchProgressEntity
            {
                UserId = UserId,
                MovieId = request.MovieId,
                EpisodeId = request.EpisodeId,
                EpisodeNumber = request.EpisodeNumber ?? 1,
                ProgressSeconds = request.ProgressSeconds,
                DurationSeconds = request.DurationSeconds > 0 ? request.DurationSeconds : (movie.DurationMinutes * 60),
                LastWatchedAtUtc = now
            });
        }
        else
        {
            item.EpisodeId = request.EpisodeId ?? item.EpisodeId;
            item.EpisodeNumber = request.EpisodeNumber ?? item.EpisodeNumber;
            item.ProgressSeconds = request.ProgressSeconds;
            if (request.DurationSeconds > 0) item.DurationSeconds = request.DurationSeconds;
            item.LastWatchedAtUtc = now;
        }

        await db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
