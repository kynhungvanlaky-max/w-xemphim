using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using WebXemPhim.Shared.Utils;
namespace WebXemPhim.Server.Services;

public sealed class DatabaseMovieService(
    IDbContextFactory<AppDbContext> contextFactory,
    IMemoryCache? cache = null) : IMovieService
{
    private static readonly IMemoryCache s_fallbackCache = new MemoryCache(new MemoryCacheOptions());
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_ipLocks = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> s_movieLocks = new();
    private readonly IMemoryCache _cache = cache ?? s_fallbackCache;
    public async Task<MovieFilterOptionsDto> GetFilterOptionsAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return new MovieFilterOptionsDto
        {
            Genres = await context.Genres.AsNoTracking().Select(g => g.Name).Distinct().OrderBy(x => x).ToListAsync(),
            Countries = await context.Movies.AsNoTracking().Select(x => x.Country).Where(x => x != "").Distinct().OrderBy(x => x).ToListAsync(),
            Years = await context.Movies.AsNoTracking().Select(x => x.ReleaseYear).Distinct().OrderByDescending(x => x).ToListAsync()
        };
    }
    public async Task<MovieDetailDto?> GetMovieByIdAsync(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var detail = MapDetail(await DetailQuery(context).SingleOrDefaultAsync(movie => movie.Id == id));
        if (detail is not null)
        {
            detail.SimilarMovies = await GetSimilarMoviesAsync(detail.Id, SimilarMovieCount);
        }
        return detail;
    }

    public async Task<MovieDetailDto?> GetMovieBySlugAsync(string slug)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var normalized = slug.ToLowerInvariant();
        var detail = MapDetail(await DetailQuery(context).SingleOrDefaultAsync(movie => movie.Slug == normalized));
        if (detail is not null)
        {
            detail.SimilarMovies = await GetSimilarMoviesAsync(detail.Id, SimilarMovieCount);
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
        count = Math.Clamp(count, 1, 50);
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context).Where(movie => movie.IsTrending)
            .OrderByDescending(movie => movie.Rating).ThenByDescending(movie => movie.ViewCount)
            .Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<List<MovieDto>> GetTopSeriesAsync(int count = 10)
    {
        if (count <= 0) return [];
        count = Math.Clamp(count, 1, 50);
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context)
            .Where(movie => movie.Type == MovieType.Series || movie.Type == MovieType.Anime)
            .OrderByDescending(movie => movie.Rating).Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<List<MovieDto>> GetLatestMoviesAsync(int count = 12)
    {
        if (count <= 0) return [];
        count = Math.Clamp(count, 1, 50);
        await using var context = await contextFactory.CreateDbContextAsync();
        var movies = await SummaryQuery(context).OrderByDescending(movie => movie.ReleaseYear)
            .ThenByDescending(movie => movie.Id).Take(count).ToListAsync();
        return movies.Select(MapSummary).ToList();
    }

    public async Task<HomeAggregateDto> GetHomeAggregateAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var featuredEntity = await SummaryQuery(context)
            .OrderByDescending(item => item.IsFeatured)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync();
        var featured = featuredEntity is null ? null : MapSummary(featuredEntity);

        var trendingEntities = await SummaryQuery(context)
            .Where(movie => movie.IsTrending)
            .OrderByDescending(movie => movie.Rating)
            .ThenByDescending(movie => movie.ViewCount)
            .Take(10)
            .ToListAsync();
        var trending = trendingEntities.Select(MapSummary).ToList();

        var latestEntities = await SummaryQuery(context)
            .OrderByDescending(movie => movie.ReleaseYear)
            .ThenByDescending(movie => movie.Id)
            .Take(12)
            .ToListAsync();
        var latest = latestEntities.Select(MapSummary).ToList();

        var seriesEntities = await SummaryQuery(context)
            .Where(movie => movie.Type == MovieType.Series || movie.Type == MovieType.Anime)
            .OrderByDescending(movie => movie.Rating)
            .Take(10)
            .ToListAsync();
        var topSeries = seriesEntities.Select(MapSummary).ToList();

        var heroList = new List<MovieDto>();
        var heroIds = new HashSet<int>();
        if (featured is not null)
        {
            heroList.Add(featured);
            heroIds.Add(featured.Id);
        }
        foreach (var m in trending)
        {
            if (heroList.Count >= 5) break;
            if (heroIds.Add(m.Id))
            {
                heroList.Add(m);
            }
        }

        return new HomeAggregateDto
        {
            Featured = featured,
            Trending = trending,
            Latest = latest,
            TopSeries = topSeries,
            HeroMovies = heroList
        };
    }

    public async Task<PagedResult<MovieDto>> FilterMoviesAsync(MovieFilterRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Movies.AsNoTracking().AsQueryable();

        // Giới hạn đầu vào công khai: pageSize do client gửi không được phép kéo toàn bộ
        // catalog hoặc tạo truy vấn quá nặng.
        var page = Math.Clamp(request.Page <= 0 ? 1 : request.Page, 1, 1_000_000);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 18 : request.PageSize, 1, 100);

        List<int>? searchMatchingIds = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var searchHandled = false;
            if (context.Database.IsNpgsql())
            {
                try
                {
                    var tsQuery = BuildPostgresTsQuery(request.Keyword);
                    if (!string.IsNullOrWhiteSpace(tsQuery))
                    {
                        var matchingIds = await QueryPostgresSearchAsync(context, tsQuery);
                        if (matchingIds.Count > 0)
                        {
                            searchMatchingIds = matchingIds;
                            query = query.Where(movie => matchingIds.Contains(movie.Id));
                        }
                        else
                        {
                            return new PagedResult<MovieDto>
                            {
                                Items = [],
                                TotalItems = 0,
                                Page = page,
                                PageSize = pageSize
                            };
                        }
                        searchHandled = true;
                    }
                }
                catch
                {
                    searchHandled = false;
                }
            }

            if (!searchHandled)
            {
                var normKeyword = VietnameseTextNormalizer.NormalizeForSearch(request.Keyword);
                var rawKeyword = request.Keyword.Trim().ToLowerInvariant();

                if (!context.Database.IsRelational())
                {
                    var candidateMovies = await query.Include(m => m.CastMembers).ToListAsync();
                    var matchedIds = candidateMovies.Where(movie =>
                    {
                        var titleNorm = VietnameseTextNormalizer.NormalizeForSearch(movie.Title);
                        var origNorm = VietnameseTextNormalizer.NormalizeForSearch(movie.OriginalTitle);
                        var dirNorm = VietnameseTextNormalizer.NormalizeForSearch(movie.Director);
                        var castNorm = string.Join(" ", movie.CastMembers.Select(c => VietnameseTextNormalizer.NormalizeForSearch(c.Name)));

                        return titleNorm.Contains(normKeyword) ||
                               origNorm.Contains(normKeyword) ||
                               dirNorm.Contains(normKeyword) ||
                               castNorm.Contains(normKeyword) ||
                               (movie.Title != null && movie.Title.ToLowerInvariant().Contains(rawKeyword)) ||
                               (movie.OriginalTitle != null && movie.OriginalTitle.ToLowerInvariant().Contains(rawKeyword));
                    }).Select(m => m.Id).ToList();

                    query = query.Where(m => matchedIds.Contains(m.Id));
                }
                else
                {
                    // Prefer the pre-normalized, persistent SearchText column. This keeps
                    // MySQL/relational providers aligned with the PostgreSQL tsvector index and
                    // the Vietnamese normalization behaviour without per-row client-side evaluation.
                    var hasSearchText = await context.Movies.AsNoTracking()
                        .AnyAsync(m => m.SearchText != null && m.SearchText != string.Empty);
                    if (hasSearchText)
                    {
                        query = query.Where(movie =>
                            movie.SearchText != null &&
                            movie.SearchText.Contains(normKeyword));
                    }
                    else
                    {
                        // Backward-compatible fallback for un-migrated/old databases.
                        query = query.Where(movie =>
                            movie.Title.ToLower().Contains(rawKeyword) ||
                            movie.OriginalTitle.ToLower().Contains(rawKeyword) ||
                            movie.Director.ToLower().Contains(rawKeyword) ||
                            movie.CastMembers.Any(member => member.Name.ToLower().Contains(rawKeyword)));
                    }
                }
            }
        }
        var genres = request.ResolveGenres();
        var countries = request.ResolveCountries();
        var years = request.ResolveYears();
        var types = request.ResolveTypes();
        // PostgreSQL folds only ASCII in LOWER(), so Vietnamese labels are resolved in .NET
        // and then compared against their exact stored values inside the database query.
        if (genres.Length > 0)
        {
            var available = await context.Genres.AsNoTracking().Select(g => new { g.Name, g.Slug }).ToListAsync();
            var matching = available.Where(g => genres.Contains(g.Name.ToLowerInvariant()) || genres.Contains(g.Slug.ToLowerInvariant()))
                                    .Select(g => g.Name).ToArray();
            query = query.Where(movie => movie.MovieGenres.Any(item => matching.Contains(item.Genre.Name)));
        }
        if (countries.Length > 0)
        {
            var available = await context.Movies.AsNoTracking().Select(movie => movie.Country).Where(c => c != "").Distinct().ToListAsync();
            var matching = available.Where(name => countries.Contains(name.ToLowerInvariant())).ToArray();
            query = query.Where(movie => matching.Contains(movie.Country));
        }
        if (years.Length > 0) query = query.Where(movie => years.Contains(movie.ReleaseYear));
        if (types.Length > 0) query = query.Where(movie => types.Contains(movie.Type));

        int totalItems;
        if (request.SortBy.Equals("relevance", StringComparison.OrdinalIgnoreCase) && searchMatchingIds != null && searchMatchingIds.Count > 0)
        {
            // Khi không có bộ lọc bổ sung (thể loại / quốc gia / năm / loại phim) thì mọi id khớp
            // tìm kiếm đều hợp lệ, nên không cần vòng truy vấn "giao nhau" thứ hai.
            var hasExtraFilters = genres.Length > 0 || countries.Length > 0 || years.Length > 0 || types.Length > 0;

            List<int> pagedIds;
            if (hasExtraFilters)
            {
                var allMatchedIdList = await query.Select(m => m.Id).ToListAsync();
                var allMatchedIdSet = allMatchedIdList.ToHashSet();
                totalItems = allMatchedIdSet.Count;

                pagedIds = searchMatchingIds
                    .Where(allMatchedIdSet.Contains)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
            else
            {
                totalItems = searchMatchingIds.Count;

                pagedIds = searchMatchingIds
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }

            // Chiếu thẳng ra DTO để chỉ cần MỘT vòng truy vấn. Dùng Include sẽ khiến EF tách
            // bản ghi thành 3 câu lệnh riêng (phim + liên kết thể loại + thể loại), làm nặng
            // đường tìm kiếm nóng.
            var pageMovies = await context.Movies.AsNoTracking()
                .Where(movie => pagedIds.Contains(movie.Id))
                .Select(movie => new MovieDto
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
                    Genres = movie.MovieGenres
                        .OrderBy(item => item.GenreId)
                        .Select(item => item.Genre.Name)
                        .ToList(),
                    Country = movie.Country,
                    ViewCount = movie.ViewCount,
                    IsTrending = movie.IsTrending,
                    IsFeatured = movie.IsFeatured,
                    TotalEpisodes = movie.TotalEpisodes,
                    CurrentEpisode = movie.CurrentEpisode
                })
                .ToListAsync();

            var rankMap = pagedIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            var sortedMovies = pageMovies.OrderBy(m => rankMap.GetValueOrDefault(m.Id)).ToList();

            return new PagedResult<MovieDto>
            {
                Items = sortedMovies,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }

        totalItems = await query.CountAsync();

        query = request.SortBy.ToLowerInvariant() switch
        {
            "rating" => query.OrderByDescending(movie => movie.Rating),
            "latest" => query.OrderByDescending(movie => movie.ReleaseYear).ThenByDescending(movie => movie.Id),
            "views" => query.OrderByDescending(movie => movie.ViewCount),
            _ => query.OrderByDescending(movie => movie.IsTrending).ThenByDescending(movie => movie.Rating)
        };

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

    /// <summary>Số phim tương tự gắn kèm mỗi phim (dùng cho trang xem và trang thông tin).</summary>
    private const int SimilarMovieCount = 8;

    public async Task<List<MovieDto>> GetSimilarMoviesAsync(int movieId, int count = 6)
    {        if (count <= 0) return [];
        count = Math.Clamp(count, 1, 50);
        await using var context = await contextFactory.CreateDbContextAsync();
        var current = await context.Movies.AsNoTracking().Include(movie => movie.MovieGenres)
            .SingleOrDefaultAsync(movie => movie.Id == movieId);
        if (current is null) return [];

        var genreIds = current.MovieGenres.Select(item => item.GenreId).ToList();
        var movies = await SummaryQuery(context)
            .Where(movie => movie.Id != movieId
                // Chỉ gợi ý những phim đã có tập phát — bấm vào phim chưa có nguồn thì người xem
                // sẽ gặp ngay màn hình "chưa có nguồn phát".
                && movie.Episodes.Any(episode => episode.Streams.Any())
                && (movie.Type == current.Type
                    || movie.MovieGenres.Any(item => genreIds.Contains(item.GenreId))))
            // Xếp theo mức tương đồng (số thể loại chung) trước, rồi mới theo điểm đánh giá —
            // nếu chỉ theo điểm đánh giá thì phim hay nhất thắng chứ không phải phim giống nhất.
            .OrderByDescending(movie => movie.MovieGenres.Count(item => genreIds.Contains(item.GenreId)))
            .ThenByDescending(movie => movie.Rating)
            .ThenByDescending(movie => movie.ViewCount)
            .Take(count).ToListAsync();
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
                    // The episode id and the stream position are what the playback URL is
                    // signed with. Leaving them at 0 produced a URL for episode 0, which the
                    // stream endpoint answers with 404 — the player then never started.
                    Id = stream.Position,
                    EpisodeId = stream.EpisodeId,
                    Position = stream.Position,
                    ServerName = stream.ServerName,
                    ServerType = stream.ServerType,
                    Quality = stream.Quality,
                    StreamUrl = stream.StreamUrl
                }).ToList()
            }).ToList()
        };
    }

    public async Task<bool> IncrementViewCountAsync(int movieId, string? clientIp = null)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(clientIp) ? $"view_cooldown:{movieId}:{clientIp}" : null;
        if (cacheKey != null)
        {
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return true;
            }

            var ipLock = s_ipLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await ipLock.WaitAsync();
            try
            {
                if (_cache.TryGetValue(cacheKey, out _))
                {
                    return true;
                }

                var updated = await ExecuteIncrementAsync(movieId);
                if (updated)
                {
                    _cache.Set(cacheKey, true, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    });
                    return true;
                }

                return false;
            }
            finally
            {
                ipLock.Release();
                if (ipLock.CurrentCount > 0)
                {
                    s_ipLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(cacheKey, ipLock));
                }
            }
        }

        return await ExecuteIncrementAsync(movieId);
    }

    private async Task<bool> ExecuteIncrementAsync(int movieId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        if (context.Database.IsRelational())
        {
            var affected = await context.Movies
                .Where(m => m.Id == movieId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ViewCount, m => m.ViewCount + 1));
            return affected > 0;
        }

        var movieLock = s_movieLocks.GetOrAdd(movieId, _ => new SemaphoreSlim(1, 1));
        await movieLock.WaitAsync();
        try
        {
            var movie = await context.Movies.FindAsync(movieId);
            if (movie is null) return false;
            movie.ViewCount++;
            var affected = await context.SaveChangesAsync();
            return affected > 0;
        }
        finally
        {
            movieLock.Release();
        }
    }

    public async Task<List<MovieDto>> SearchMoviesAsync(string keyword, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];
        var safeLimit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
        var result = await FilterMoviesAsync(new MovieFilterRequest
        {
            Keyword = keyword.Trim(),
            SortBy = "relevance",
            Page = 1,
            PageSize = safeLimit
        });
        return result.Items;
    }

    /// <summary>
    /// Builds a PostgreSQL <c>tsquery</c> expression from a free-text keyword.
    /// The keyword is normalized with the shared Vietnamese normalizer so that "Xứ Cát" and
    /// "xu cat" produce the same query, and every token is quoted so that words such as
    /// "and"/"or" cannot be interpreted as tsquery operators.
    /// </summary>
    public static string BuildPostgresTsQuery(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return string.Empty;

        var normalized = VietnameseTextNormalizer.NormalizeForSearch(keyword);
        var tokens = Regex.Matches(normalized, @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tokens.Count == 0) return string.Empty;

        return string.Join(" & ", tokens.Select(token => $"'{token.Replace("'", "''")}':*"));
    }

    /// <summary>
    /// Runs the PostgreSQL full-text search and returns the matching movie ids ordered by
    /// relevance (the same order the FTS5 implementation produced).
    /// </summary>
    private static async Task<List<int>> QueryPostgresSearchAsync(AppDbContext context, string tsQuery)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();
        try
        {
            var matchingIds = new List<int>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT m.""Id""
  FROM movies m
  CROSS JOIN to_tsquery('simple', @query) q
 WHERE m.""search_vector"" @@ q
 ORDER BY ts_rank_cd(m.""search_vector"", q) DESC, m.""Id"" ASC;";
            var pQuery = cmd.CreateParameter();
            pQuery.ParameterName = "@query";
            pQuery.Value = tsQuery;
            cmd.Parameters.Add(pQuery);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                matchingIds.Add(reader.GetInt32(0));
            }

            return matchingIds;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
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
