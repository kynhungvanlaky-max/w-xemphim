using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Services;

public sealed class DatabaseMovieService(IDbContextFactory<AppDbContext> contextFactory) : IMovieService
{
    public async Task<MovieFilterOptionsDto> GetFilterOptionsAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return new MovieFilterOptionsDto
        {
            Genres = await context.Movies.SelectMany(x => x.MovieGenres.Select(g => g.Genre.Name)).Distinct().OrderBy(x => x).ToListAsync(),
            Countries = await context.Movies.Select(x => x.Country).Where(x => x != "").Distinct().OrderBy(x => x).ToListAsync(),
            Years = await context.Movies.Select(x => x.ReleaseYear).Distinct().OrderByDescending(x => x).ToListAsync()
        };
    }
    public async Task<MovieDetailDto?> GetMovieByIdAsync(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var detail = MapDetail(await DetailQuery(context).SingleOrDefaultAsync(movie => movie.Id == id));
        if (detail is not null)
        {
            detail.SimilarMovies = await GetSimilarMoviesAsync(detail.Id, 6);
        }
        return detail;
    }

    public async Task<MovieDetailDto?> GetMovieBySlugAsync(string slug)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var normalized = slug.ToLower();
        var detail = MapDetail(await DetailQuery(context).SingleOrDefaultAsync(movie => movie.Slug.ToLower() == normalized));
        if (detail is not null)
        {
            detail.SimilarMovies = await GetSimilarMoviesAsync(detail.Id, 6);
        }
        return detail;
    }

    public async Task<MovieDto?> GetFeaturedMovieAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await SummaryQuery(context).OrderByDescending(item => item.IsFeatured).ThenBy(item => item.Id).FirstOrDefaultAsync();
        return movie is null ? null : MapSummary(movie);
    }

    public async Task<List<MovieDto>> GetTrendingMoviesAsync(int count = 10)
    {
        if (count <= 0) return [];
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context).Where(movie => movie.IsTrending)
            .OrderByDescending(movie => movie.Rating).ThenByDescending(movie => movie.ViewCount)
            .Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<List<MovieDto>> GetTopSeriesAsync(int count = 10)
    {
        if (count <= 0) return [];
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context)
            .Where(movie => movie.Type == MovieType.Series || movie.Type == MovieType.Anime)
            .OrderByDescending(movie => movie.Rating).Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<List<MovieDto>> GetLatestMoviesAsync(int count = 12)
    {
        if (count <= 0) return [];
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context).OrderByDescending(movie => movie.ReleaseYear)
            .ThenByDescending(movie => movie.Id).Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Movies.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(movie =>
                movie.Title.ToLower().Contains(keyword) ||
                movie.OriginalTitle.ToLower().Contains(keyword) ||
                movie.Director.ToLower().Contains(keyword) ||
                movie.CastMembers.Any(member => member.Name.ToLower().Contains(keyword)));
        }
        var genres = request.ResolveGenres();
        var countries = request.ResolveCountries();
        var years = request.ResolveYears();
        var types = request.ResolveTypes();
        // SQLite LOWER handles ASCII only. Resolve Vietnamese labels in .NET,
        // then compare their exact stored values inside the database query.
        if (genres.Length > 0)
        {
            var available = await context.Movies.SelectMany(movie => movie.MovieGenres.Select(item => item.Genre.Name)).Distinct().ToListAsync();
            var matching = available.Where(name => genres.Contains(name.ToLowerInvariant())).ToArray();
            query = query.Where(movie => movie.MovieGenres.Any(item => matching.Contains(item.Genre.Name)));
        }
        if (countries.Length > 0)
        {
            var available = await context.Movies.Select(movie => movie.Country).Distinct().ToListAsync();
            var matching = available.Where(name => countries.Contains(name.ToLowerInvariant())).ToArray();
            query = query.Where(movie => matching.Contains(movie.Country));
        }
        if (years.Length > 0) query = query.Where(movie => years.Contains(movie.ReleaseYear));
        if (types.Length > 0) query = query.Where(movie => types.Contains(movie.Type));

        query = request.SortBy.ToLowerInvariant() switch
        {
            "rating" => query.OrderByDescending(movie => movie.Rating),
            "latest" => query.OrderByDescending(movie => movie.ReleaseYear).ThenByDescending(movie => movie.Id),
            "views" => query.OrderByDescending(movie => movie.ViewCount),
            _ => query.OrderByDescending(movie => movie.IsTrending).ThenByDescending(movie => movie.Rating)
        };

        var totalItems = await query.CountAsync();
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 18 : request.PageSize;
        var movies = await query.Include(movie => movie.MovieGenres).ThenInclude(item => item.Genre)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedResult<MovieDto>
        {
            Items = movies.Select(MapSummary).ToList(),
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<List<MovieDto>> GetSimilarMoviesAsync(int movieId, int count = 6)
    {
        if (count <= 0) return [];
        await using var context = await contextFactory.CreateDbContextAsync();
        var current = await context.Movies.AsNoTracking().Include(movie => movie.MovieGenres)
            .SingleOrDefaultAsync(movie => movie.Id == movieId);
        if (current is null) return [];

        var genreIds = current.MovieGenres.Select(item => item.GenreId).ToList();
        var movies = await SummaryQuery(context)
            .Where(movie => movie.Id != movieId &&
                (movie.Type == current.Type || movie.MovieGenres.Any(item => genreIds.Contains(item.GenreId))))
            .OrderByDescending(movie => movie.Rating).Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    private static IQueryable<MovieEntity> SummaryQuery(AppDbContext context) =>
        context.Movies.AsNoTracking().Include(movie => movie.MovieGenres).ThenInclude(item => item.Genre);

    private static IQueryable<MovieEntity> DetailQuery(AppDbContext context) =>
        SummaryQuery(context)
            .Include(movie => movie.CastMembers)
            .Include(movie => movie.Episodes).ThenInclude(episode => episode.Streams)
            .AsSplitQuery();

    private static MovieDto MapSummary(MovieEntity movie) => new()
    {
        Id = movie.Id,
        Title = movie.Title,
        OriginalTitle = movie.OriginalTitle,
        Slug = movie.Slug,
        Overview = movie.Overview,
        PosterUrl = movie.PosterUrl,
        BackdropUrl = movie.BackdropUrl,
        TrailerUrl = movie.TrailerUrl,
        Rating = movie.Rating,
        VoteCount = movie.VoteCount,
        ReleaseYear = movie.ReleaseYear,
        DurationMinutes = movie.DurationMinutes,
        AgeRating = movie.AgeRating,
        Quality = movie.Quality,
        Type = movie.Type,
        Genres = movie.MovieGenres.OrderBy(item => item.GenreId).Select(item => item.Genre.Name).ToList(),
        Country = movie.Country,
        ViewCount = movie.ViewCount,
        IsTrending = movie.IsTrending,
        IsFeatured = movie.IsFeatured,
        TotalEpisodes = movie.TotalEpisodes,
        CurrentEpisode = movie.CurrentEpisode
    };

    private static MovieDetailDto? MapDetail(MovieEntity? movie)
    {
        if (movie is null) return null;
        var summary = MapSummary(movie);
        return new MovieDetailDto
        {
            Id = summary.Id,
            Title = summary.Title,
            OriginalTitle = summary.OriginalTitle,
            Slug = summary.Slug,
            Overview = summary.Overview,
            PosterUrl = summary.PosterUrl,
            BackdropUrl = summary.BackdropUrl,
            TrailerUrl = summary.TrailerUrl,
            Rating = summary.Rating,
            VoteCount = summary.VoteCount,
            ReleaseYear = summary.ReleaseYear,
            DurationMinutes = summary.DurationMinutes,
            AgeRating = summary.AgeRating,
            Quality = summary.Quality,
            Type = summary.Type,
            Genres = summary.Genres,
            Country = summary.Country,
            ViewCount = summary.ViewCount,
            IsTrending = summary.IsTrending,
            IsFeatured = summary.IsFeatured,
            TotalEpisodes = summary.TotalEpisodes,
            CurrentEpisode = summary.CurrentEpisode,
            Director = movie.Director,
            Cast = movie.CastMembers.OrderBy(member => member.Position).Select(member => member.Name).ToList(),
            Episodes = movie.Episodes.OrderBy(episode => episode.EpisodeNumber).Select(episode => new EpisodeDto
            {
                Id = episode.Id,
                MovieId = episode.MovieId,
                EpisodeNumber = episode.EpisodeNumber,
                Title = episode.Title,
                DurationMinutes = episode.DurationMinutes,
                ThumbnailUrl = episode.ThumbnailUrl,
                Overview = episode.Overview,
                Streams = episode.Streams.OrderBy(stream => stream.Position).Select(stream => new StreamSourceDto
                {
                    ServerName = stream.ServerName,
                    ServerType = stream.ServerType,
                    Quality = stream.Quality,
                    StreamUrl = stream.StreamUrl
                }).ToList()
            }).ToList()
        };
    }
}

public sealed class DatabaseGenreService(IDbContextFactory<AppDbContext> contextFactory) : IGenreService
{
    public async Task<List<GenreDto>> GetAllGenresAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Genres.AsNoTracking().OrderBy(genre => genre.Id).Select(genre => new GenreDto
        {
            Id = genre.Id,
            Name = genre.Name,
            Slug = genre.Slug,
            MovieCount = genre.MovieGenres.Count
        }).ToListAsync();
    }
}
