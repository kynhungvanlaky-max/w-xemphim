using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Services;

public interface IMovieService
{
    Task<MovieFilterOptionsDto> GetFilterOptionsAsync();
    Task<MovieDetailDto?> GetMovieByIdAsync(int id);
    Task<MovieDetailDto?> GetMovieBySlugAsync(string slug);
    Task<MovieDto?> GetFeaturedMovieAsync();
    Task<List<MovieDto>> GetTrendingMoviesAsync(int count = 10);
    Task<List<MovieDto>> GetTopSeriesAsync(int count = 10);
    Task<List<MovieDto>> GetLatestMoviesAsync(int count = 12);
    Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request);
    Task<List<MovieDto>> GetSimilarMoviesAsync(int movieId, int count = 6);
}

public class MovieService : IMovieService
{
    public Task<MovieFilterOptionsDto> GetFilterOptionsAsync() => Task.FromResult(new MovieFilterOptionsDto
    {
        Genres = MockMovieDatabase.Movies.SelectMany(x => x.Genres).Distinct().Order().ToList(),
        Countries = MockMovieDatabase.Movies.Select(x => x.Country).Distinct().Order().ToList(),
        Years = MockMovieDatabase.Movies.Select(x => x.ReleaseYear).Distinct().OrderDescending().ToList()
    });
    public Task<MovieDetailDto?> GetMovieByIdAsync(int id)
    {
        var movie = MockMovieDatabase.Movies.FirstOrDefault(m => m.Id == id);
        return Task.FromResult(movie);
    }

    public Task<MovieDetailDto?> GetMovieBySlugAsync(string slug)
    {
        var movie = MockMovieDatabase.Movies.FirstOrDefault(m => m.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(movie);
    }

    public Task<MovieDto?> GetFeaturedMovieAsync()
    {
        var featured = MockMovieDatabase.Movies.FirstOrDefault(m => m.IsFeatured)
                       ?? MockMovieDatabase.Movies.FirstOrDefault();
        return Task.FromResult<MovieDto?>(featured);
    }

    public Task<List<MovieDto>> GetTrendingMoviesAsync(int count = 10)
    {
        var trending = MockMovieDatabase.Movies
            .Where(m => m.IsTrending)
            .OrderByDescending(m => m.Rating)
            .ThenByDescending(m => m.ViewCount)
            .Take(count)
            .Cast<MovieDto>()
            .ToList();

        return Task.FromResult(trending);
    }

    public Task<List<MovieDto>> GetTopSeriesAsync(int count = 10)
    {
        var series = MockMovieDatabase.Movies
            .Where(m => m.Type == Shared.Enums.MovieType.Series || m.Type == Shared.Enums.MovieType.Anime)
            .OrderByDescending(m => m.Rating)
            .Take(count)
            .Cast<MovieDto>()
            .ToList();

        return Task.FromResult(series);
    }

    public Task<List<MovieDto>> GetLatestMoviesAsync(int count = 12)
    {
        var latest = MockMovieDatabase.Movies
            .OrderByDescending(m => m.ReleaseYear)
            .ThenByDescending(m => m.Id)
            .Take(count)
            .Cast<MovieDto>()
            .ToList();

        return Task.FromResult(latest);
    }

    public Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request)
    {
        var query = MockMovieDatabase.Movies.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(m =>
                m.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                m.OriginalTitle.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                m.Director.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                m.Cast.Any(c => c.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        var genres = request.ResolveGenres();
        var countries = request.ResolveCountries();
        var years = request.ResolveYears();
        var types = request.ResolveTypes();
        if (genres.Length > 0) query = query.Where(m => m.Genres.Any(g => genres.Contains(g.ToLowerInvariant())));
        if (countries.Length > 0) query = query.Where(m => countries.Contains(m.Country.ToLowerInvariant()));
        if (years.Length > 0) query = query.Where(m => years.Contains(m.ReleaseYear));
        if (types.Length > 0) query = query.Where(m => types.Contains(m.Type));

        query = request.SortBy.ToLowerInvariant() switch
        {
            "rating" => query.OrderByDescending(m => m.Rating),
            "latest" => query.OrderByDescending(m => m.ReleaseYear).ThenByDescending(m => m.Id),
            "views" => query.OrderByDescending(m => m.ViewCount),
            _ => query.OrderByDescending(m => m.IsTrending).ThenByDescending(m => m.Rating)
        };

        var totalItems = query.Count();
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 18 : request.PageSize;

        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Cast<MovieDto>()
            .ToList();

        return Task.FromResult(new PagedResult<MovieDto>
        {
            Items = items,
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize
        });
    }

    public Task<List<MovieDto>> GetSimilarMoviesAsync(int movieId, int count = 6)
    {
        var current = MockMovieDatabase.Movies.FirstOrDefault(m => m.Id == movieId);
        if (current == null) return Task.FromResult(new List<MovieDto>());

        var similar = MockMovieDatabase.Movies
            .Where(m => m.Id != movieId && (m.Type == current.Type || m.Genres.Any(g => current.Genres.Contains(g))))
            .OrderByDescending(m => m.Rating)
            .Take(count)
            .Cast<MovieDto>()
            .ToList();

        return Task.FromResult(similar);
    }
}
