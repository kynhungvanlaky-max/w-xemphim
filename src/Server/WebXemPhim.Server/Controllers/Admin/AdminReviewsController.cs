using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/reviews")]
public sealed class AdminReviewsController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminReviewDto>>>> GetReviews(
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? minRating = null,
        [FromQuery] int? maxRating = null,
        [FromQuery] string? sortBy = null)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(r => r.Comment.ToLower().Contains(s)
                                  || (r.Movie != null && r.Movie.Title.ToLower().Contains(s))
                                  || (r.User != null && (r.User.Email.ToLower().Contains(s) || r.User.FullName.ToLower().Contains(s))));
        }

        var statusFilter = status?.Trim().ToLower();
        if (statusFilter == "reported")
        {
            query = query.Where(r => r.IsReported);
        }
        else if (statusFilter == "replied")
        {
            query = query.Where(r => r.AdminReply != null && r.AdminReply != string.Empty);
        }
        else if (statusFilter == "pending")
        {
            query = query.Where(r => r.AdminReply == null || r.AdminReply == string.Empty);
        }

        if (minRating.HasValue && minRating.Value > 0)
        {
            var min = minRating.Value;
            query = query.Where(r => r.Rating >= min);
        }

        if (maxRating.HasValue && maxRating.Value > 0)
        {
            var max = maxRating.Value;
            query = query.Where(r => r.Rating <= max);
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "rating_asc" => query.OrderBy(r => r.Rating),
            "rating_desc" => query.OrderByDescending(r => r.Rating),
            "oldest" => query.OrderBy(r => r.CreatedAtUtc),
            _ => query.OrderByDescending(r => r.CreatedAtUtc)
        };

        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 1000) pageSize = 1000;
        var totalItems = await query.CountAsync();
        var reviews = await query
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                MovieId = r.MovieId,
                MovieTitle = r.Movie != null ? r.Movie.Title : string.Empty,
                MoviePosterUrl = r.Movie != null ? r.Movie.PosterUrl : string.Empty,
                UserId = r.UserId,
                UserFullName = r.User != null ? r.User.FullName : string.Empty,
                UserEmail = r.User != null ? r.User.Email : string.Empty,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                AdminReply = r.AdminReply,
                AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                IsReported = r.IsReported
            })
            .ToListAsync();

        var pagedResult = new PagedResult<AdminReviewDto>
        {
            Items = reviews,
            TotalItems = totalItems,
            Page = Math.Max(page, 1),
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<AdminReviewDto>>.Ok(pagedResult));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteReview(Guid id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var review = await context.Reviews.FindAsync(id);
        if (review is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy đánh giá."));
        }

        var movieId = review.MovieId;
        context.Reviews.Remove(review);

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();

            var movieReviews = await context.Reviews
                .Where(r => r.MovieId == movieId && r.Id != id)
                .ToListAsync();
            var movie = await context.Movies.FindAsync(movieId);
            if (movie is not null)
            {
                movie.VoteCount = movieReviews.Count;
                movie.Rating = movieReviews.Count > 0
                    ? Math.Round(movieReviews.Average(r => (double)r.Rating), 1)
                    : 0;
                await context.SaveChangesAsync();
            }

            await LogAuditAsync(context, "DELETE_REVIEW", "Review", id.ToString(), $"Đã xóa đánh giá của người dùng {review.UserId}.");
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

        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa đánh giá thành công."));
    }

    [HttpPost("{id:guid}/reply")]
    public async Task<ActionResult<ApiResponse<AdminReviewDto>>> ReplyToReview(Guid id, [FromBody] AdminReviewModerationRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var review = await context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review is null) return NotFound(ApiResponse<AdminReviewDto>.Fail("Không tìm thấy đánh giá."));

        if (request.AdminReply != null)
        {
            review.AdminReply = request.AdminReply.Trim();
            review.AdminRepliedAtUtc = UtcNow;
        }

        if (request.IsReported.HasValue)
        {
            review.IsReported = request.IsReported.Value;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "MODERATE_REVIEW", "Review", id.ToString(), $"Kiểm duyệt đánh giá của '{(review.User != null ? review.User.Email : review.UserId.ToString())}' trên phim '{(review.Movie != null ? review.Movie.Title : review.MovieId.ToString())}'.");
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

        var dto = new AdminReviewDto
        {
            Id = review.Id,
            MovieId = review.MovieId,
            MovieTitle = review.Movie != null ? review.Movie.Title : string.Empty,
            MoviePosterUrl = review.Movie != null ? review.Movie.PosterUrl : string.Empty,
            UserId = review.UserId,
            UserFullName = review.User != null ? review.User.FullName : string.Empty,
            UserEmail = review.User != null ? review.User.Email : string.Empty,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = new DateTimeOffset(review.CreatedAtUtc, TimeSpan.Zero),
            AdminReply = review.AdminReply,
            AdminRepliedAt = review.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(review.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
            IsReported = review.IsReported
        };

        return Ok(ApiResponse<AdminReviewDto>.Ok(dto, "Cập nhật kiểm duyệt đánh giá thành công."));
    }
}
