using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController(IReviewService reviewService) : ControllerBase
{
    [HttpGet("movie/{movieId:int}")]
    public async Task<ActionResult<ApiResponse<MovieReviewsDto>>> GetForMovie(int movieId)
    {
        var result = await reviewService.GetMovieReviewsAsync(movieId);
        return result is null
            ? NotFound(ApiResponse<MovieReviewsDto>.Fail($"Không tìm thấy phim với ID: {movieId}"))
            : Ok(ApiResponse<MovieReviewsDto>.Ok(result));
    }

    [Authorize]
    [HttpPut("movie/{movieId:int}")]
    [EnableRateLimiting("UserSubmissions")]
    public async Task<ActionResult<ApiResponse<ReviewDto>>> Upsert(int movieId, UpsertReviewRequest request)
    {
        if (await reviewService.GetMovieReviewsAsync(movieId) is null)
            return NotFound(ApiResponse<ReviewDto>.Fail($"Không tìm thấy phim với ID: {movieId}"));

        var review = await reviewService.UpsertAsync(movieId, CurrentUserId(), request);
        return review is null
            ? BadRequest(ApiResponse<ReviewDto>.Fail("Không thể lưu đánh giá."))
            : Ok(ApiResponse<ReviewDto>.Ok(review, "Đánh giá của bạn đã được lưu."));
    }

    [Authorize]
    [HttpDelete("movie/{movieId:int}")]
    [EnableRateLimiting("UserSubmissions")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int movieId)
    {
        var removed = await reviewService.DeleteAsync(movieId, CurrentUserId());
        return removed
            ? Ok(ApiResponse<bool>.Ok(true, "Đã xóa đánh giá."))
            : NotFound(ApiResponse<bool>.Fail("Bạn chưa đánh giá phim này."));
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
