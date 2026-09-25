using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController, Authorize, Route("api/playback-issues")]
public sealed class PlaybackIssuesController(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("UserSubmissions")]
    public async Task<ActionResult<ApiResponse<Guid>>> Create(PlaybackIssueRequest request)
    {
        var description = request.Description.Trim();
        if (description.Length < 5)
            return BadRequest(ApiResponse<Guid>.Fail("Vui lòng mô tả lỗi bằng ít nhất 5 ký tự."));
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Episodes.AnyAsync(episode => episode.Id == request.EpisodeId && episode.MovieId == request.MovieId))
            return NotFound(ApiResponse<Guid>.Fail("Không tìm thấy tập phim cần báo lỗi."));
        var issue = new PlaybackIssueEntity
        {
            Id = Guid.NewGuid(), UserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            MovieId = request.MovieId, EpisodeId = request.EpisodeId,
            Description = description, CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.PlaybackIssues.Add(issue);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<Guid>.Ok(issue.Id, "Đã lưu báo lỗi. Cảm ơn bạn đã phản hồi."));
    }
}
