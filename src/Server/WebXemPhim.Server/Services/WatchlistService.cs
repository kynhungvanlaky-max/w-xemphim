using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public interface IWatchlistService
{
    Task<List<WatchlistItemDto>> GetWatchlistAsync();
    Task<bool> AddToWatchlistAsync(int movieId);
    Task<bool> RemoveFromWatchlistAsync(int movieId);
    Task<bool> IsInWatchlistAsync(int movieId);
}

public class WatchlistService : IWatchlistService
{
    private static readonly List<WatchlistItemDto> _watchlist =
    [
        new()
        {
            MovieId = 1,
            MovieTitle = "Xứ Cát: Phần Ba - Đấng Tiên Tri",
            PosterUrl = "/images/movies/movie-1-poster.jpg",
            Quality = "4K UHD",
            Rating = 8.9,
            ReleaseYear = 2026,
            AddedAt = DateTime.UtcNow.AddDays(-2),
            ProgressPercent = 45,
            LastWatchedEpisode = "Tập 1"
        },
        new()
        {
            MovieId = 3,
            MovieTitle = "Arcane: Liên Minh Huyền Thoại (Mùa 2)",
            PosterUrl = "/images/movies/movie-3-poster.jpg",
            Quality = "4K Dolby",
            Rating = 9.0,
            ReleaseYear = 2024,
            AddedAt = DateTime.UtcNow.AddDays(-5),
            ProgressPercent = 80,
            LastWatchedEpisode = "Tập 3: Mắt Bão Zaun"
        }
    ];

    private static readonly Lock _lock = new();

    public Task<List<WatchlistItemDto>> GetWatchlistAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_watchlist.OrderByDescending(w => w.AddedAt).ToList());
        }
    }

    public Task<bool> AddToWatchlistAsync(int movieId)
    {
        lock (_lock)
        {
            if (_watchlist.Any(w => w.MovieId == movieId))
            {
                return Task.FromResult(true);
            }

            var movie = MockMovieDatabase.Movies.FirstOrDefault(m => m.Id == movieId);
            if (movie == null) return Task.FromResult(false);

            _watchlist.Add(new WatchlistItemDto
            {
                MovieId = movie.Id,
                MovieTitle = movie.Title,
                PosterUrl = movie.PosterUrl,
                Quality = movie.Quality,
                Rating = movie.Rating,
                ReleaseYear = movie.ReleaseYear,
                AddedAt = DateTime.UtcNow,
                ProgressPercent = 0
            });

            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveFromWatchlistAsync(int movieId)
    {
        lock (_lock)
        {
            var item = _watchlist.FirstOrDefault(w => w.MovieId == movieId);
            if (item != null)
            {
                _watchlist.Remove(item);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsInWatchlistAsync(int movieId)
    {
        lock (_lock)
        {
            var exists = _watchlist.Any(w => w.MovieId == movieId);
            return Task.FromResult(exists);
        }
    }
}
