using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Services;

public interface IReviewService
{
    Task<MovieReviewsDto?> GetMovieReviewsAsync(int movieId);
    Task<ReviewDto?> UpsertAsync(int movieId, Guid userId, UpsertReviewRequest request);
    Task<bool> DeleteAsync(int movieId, Guid userId);
}

public sealed class ReviewService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider) : IReviewService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> MovieLocks = new();

    public async Task<MovieReviewsDto?> GetMovieReviewsAsync(int movieId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        if (!await context.Movies.AnyAsync(movie => movie.Id == movieId)) return null;
        var reviews = await context.Reviews.AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.MovieId == movieId)
            .OrderByDescending(review => review.UpdatedAtUtc)
            .ToListAsync();
        return new MovieReviewsDto
        {
            MovieId = movieId,
            TotalReviews = reviews.Count,
            AverageRating = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(review => review.Rating), 1),
            Reviews = reviews.Select(MapReview).ToList()
        };
    }

    public async Task<ReviewDto?> UpsertAsync(int movieId, Guid userId, UpsertReviewRequest request)
    {
        if (request.Rating is < 1 or > 10) return null;

        var movieLock = MovieLocks.GetOrAdd(movieId, _ => new SemaphoreSlim(1, 1));
        await movieLock.WaitAsync();
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            if (!await context.Movies.AnyAsync(movie => movie.Id == movieId)) return null;
            var user = await context.Users.SingleOrDefaultAsync(item => item.Id == userId);
            if (user is null) return null;
            var review = await context.Reviews
                .Include(item => item.User)
                .SingleOrDefaultAsync(item => item.MovieId == movieId && item.UserId == userId);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (review is null)
            {
                review = new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = movieId,
                    UserId = userId,
                    User = user,
                    CreatedAtUtc = now
                };
                context.Reviews.Add(review);
            }

            review.Rating = request.Rating;
            review.Comment = request.Comment.Trim();
            review.UpdatedAtUtc = now;
            await context.SaveChangesAsync();

            await RecalculateMovieRatingAsync(context, movieId);

            return MapReview(review);
        }
        finally
        {
            movieLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(int movieId, Guid userId)
    {
        var movieLock = MovieLocks.GetOrAdd(movieId, _ => new SemaphoreSlim(1, 1));
        await movieLock.WaitAsync();
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            var review = await context.Reviews.SingleOrDefaultAsync(item => item.MovieId == movieId && item.UserId == userId);
            if (review is null) return false;
            context.Reviews.Remove(review);
            await context.SaveChangesAsync();

            await RecalculateMovieRatingAsync(context, movieId);

            return true;
        }
        finally
        {
            movieLock.Release();
        }
    }

    private static async Task RecalculateMovieRatingAsync(AppDbContext context, int movieId)
    {
        var movie = await context.Movies.FindAsync(movieId);
        if (movie is null) return;

        var movieReviews = await context.Reviews
            .Where(r => r.MovieId == movieId)
            .ToListAsync();

        movie.VoteCount = movieReviews.Count;
        movie.Rating = movieReviews.Count > 0
            ? Math.Round(movieReviews.Average(r => (double)r.Rating), 1)
            : 0;

        await context.SaveChangesAsync();
    }

    private static ReviewDto MapReview(ReviewEntity review) => new()
    {
        Id = review.Id,
        MovieId = review.MovieId,
        UserId = review.UserId,
        UserName = review.User.FullName,
        UserInitials = GetInitials(review.User.FullName),
        Rating = review.Rating,
        Comment = review.Comment,
        CreatedAt = review.CreatedAtUtc,
        UpdatedAt = review.UpdatedAtUtc
    };

    private static string GetInitials(string fullName)
    {
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(words.TakeLast(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
