using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Tests.ServerTests;

public class MovieServiceTests : IAsyncLifetime
{
    private readonly DbContextOptions<AppDbContext> _options;
    private DatabaseMovieService _service = null!;

    public MovieServiceTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"movie-service-tests-{Guid.NewGuid():N}")
            .Options;
    }

    public async Task InitializeAsync()
    {
        await using var context = new AppDbContext(_options);
        await DatabaseSeeder.SeedCatalogAsync(context);
        var factory = new TestDbContextFactory(_options);
        _service = new DatabaseMovieService(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetFeaturedMovieAsync_ReturnsTheConfiguredFeaturedMovie()
    {
        var featured = await _service.GetFeaturedMovieAsync();

        Assert.NotNull(featured);
        Assert.True(featured.IsFeatured);
        Assert.Equal(1, featured.Id);
        Assert.NotEmpty(featured.Title);
        Assert.NotEmpty(featured.PosterUrl);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    public async Task GetMovieByIdAsync_WithKnownId_ReturnsCompleteMovie(int id)
    {
        var movie = await _service.GetMovieByIdAsync(id);

        Assert.NotNull(movie);
        Assert.Equal(id, movie.Id);
        Assert.NotEmpty(movie.Title);
        Assert.NotEmpty(movie.Slug);
        Assert.NotEmpty(movie.Episodes);
        Assert.All(movie.Episodes, episode => Assert.Equal(id, episode.MovieId));
        Assert.NotEmpty(movie.Cast);
        Assert.NotEmpty(movie.Director);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(999999)]
    [InlineData(int.MaxValue)]
    public async Task GetMovieByIdAsync_WithUnknownId_ReturnsNull(int id)
    {
        Assert.Null(await _service.GetMovieByIdAsync(id));
    }

    [Theory]
    [InlineData("dune-part-three", 1)]
    [InlineData("DUNE-PART-THREE", 1)]
    [InlineData("Interstellar", 7)]
    public async Task GetMovieBySlugAsync_IsCaseInsensitive(string slug, int expectedId)
    {
        var movie = await _service.GetMovieBySlugAsync(slug);

        Assert.NotNull(movie);
        Assert.Equal(expectedId, movie.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData(" dune-part-three ")]
    public async Task GetMovieBySlugAsync_WithNonMatchingSlug_ReturnsNull(string slug)
    {
        Assert.Null(await _service.GetMovieBySlugAsync(slug));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetTrendingMoviesAsync_WithNonPositiveCount_ReturnsEmpty(int count)
    {
        Assert.Empty(await _service.GetTrendingMoviesAsync(count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task GetTrendingMoviesAsync_ReturnsOnlyTrendingMoviesInRankingOrder(int count)
    {
        var result = await _service.GetTrendingMoviesAsync(count);

        Assert.True(result.Count <= count);
        Assert.All(result, movie => Assert.True(movie.IsTrending));
        AssertOrdered(result, (left, right) =>
            left.Rating > right.Rating ||
            left.Rating.Equals(right.Rating) && left.ViewCount >= right.ViewCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task GetTopSeriesAsync_WithNonPositiveCount_ReturnsEmpty(int count)
    {
        Assert.Empty(await _service.GetTopSeriesAsync(count));
    }

    [Fact]
    public async Task GetTopSeriesAsync_ReturnsOnlySeriesAndAnimeSortedByRating()
    {
        var result = await _service.GetTopSeriesAsync(100);

        Assert.NotEmpty(result);
        Assert.All(result, movie =>
            Assert.True(movie.Type is MovieType.Series or MovieType.Anime));
        AssertOrdered(result, (left, right) => left.Rating >= right.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task GetLatestMoviesAsync_WithNonPositiveCount_ReturnsEmpty(int count)
    {
        Assert.Empty(await _service.GetLatestMoviesAsync(count));
    }

    [Fact]
    public async Task GetLatestMoviesAsync_SortsByYearThenIdDescending()
    {
        var result = await _service.GetLatestMoviesAsync(100);

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.Count);
        AssertOrdered(result, (left, right) =>
            left.ReleaseYear > right.ReleaseYear ||
            left.ReleaseYear == right.ReleaseYear && left.Id >= right.Id);
    }

    [Theory]
    [InlineData("Dune", 1)]
    [InlineData("dUnE", 1)]
    [InlineData("Christopher Nolan", 4)]
    [InlineData("Zendaya", 1)]
    [InlineData("  Oppenheimer  ", 2)]
    public async Task FilterMoviesAsync_KeywordSearchesExpectedFields(
        string keyword,
        int expectedMovieId)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest { Keyword = keyword });

        Assert.Contains(result.Items, movie => movie.Id == expectedMovieId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FilterMoviesAsync_BlankKeywordDoesNotFilter(string? keyword)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Keyword = keyword,
            PageSize = 100
        });

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.TotalItems);
    }

    [Fact]
    public async Task FilterMoviesAsync_UnknownKeywordReturnsEmptyPage()
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Keyword = "definitely-not-a-real-movie"
        });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItems);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task FilterMoviesAsync_GenreIsCaseInsensitive()
    {
        const string genre = "anime";
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Genre = genre,
            PageSize = 100
        });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, movie =>
            Assert.Contains(movie.Genres, value => value.Equals(genre, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("")]
    public async Task FilterMoviesAsync_AllOrBlankGenreDoesNotFilter(string genre)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Genre = genre,
            PageSize = 100
        });

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.TotalItems);
    }

    [Theory]
    [InlineData("Nhật Bản", 9)]
    [InlineData("nhật bản", 9)]
    [InlineData("Hàn Quốc", 6)]
    public async Task FilterMoviesAsync_FiltersCountryCaseInsensitively(
        string country,
        int expectedCount)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Country = country,
            PageSize = 100
        });

        Assert.Equal(expectedCount, result.TotalItems);
        Assert.All(result.Items, movie =>
            Assert.Equal(country, movie.Country, ignoreCase: true));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("")]
    public async Task FilterMoviesAsync_AllOrBlankCountryDoesNotFilter(string country)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Country = country,
            PageSize = 100
        });

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.TotalItems);
    }

    [Theory]
    [InlineData(2024, 10)]
    [InlineData(2026, 3)]
    [InlineData(1900, 0)]
    public async Task FilterMoviesAsync_FiltersReleaseYear(int year, int expectedCount)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Year = year,
            PageSize = 100
        });

        Assert.Equal(expectedCount, result.TotalItems);
        Assert.All(result.Items, movie => Assert.Equal(year, movie.ReleaseYear));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FilterMoviesAsync_NonPositiveYearDoesNotFilter(int year)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Year = year,
            PageSize = 100
        });

        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.TotalItems);
    }

    [Theory]
    [InlineData(MovieType.Movie, 19)]
    [InlineData(MovieType.Series, 11)]
    [InlineData(MovieType.Anime, 10)]
    public async Task FilterMoviesAsync_FiltersMovieType(MovieType type, int expectedCount)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Type = type,
            PageSize = 100
        });

        Assert.Equal(expectedCount, result.TotalItems);
        Assert.All(result.Items, movie => Assert.Equal(type, movie.Type));
    }

    [Fact]
    public async Task FilterMoviesAsync_CombinesAllPredicatesWithAnd()
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            Keyword = "Shadow",
            Country = "Nhật Bản",
            Type = MovieType.Anime,
            Year = 2025,
            PageSize = 100
        });

        var movie = Assert.Single(result.Items);
        Assert.Equal(5, movie.Id);
    }

    [Theory]
    [InlineData("rating")]
    [InlineData("RATING")]
    public async Task FilterMoviesAsync_SortsByRatingDescending(string sortBy)
    {
        var result = await FilterAll(sortBy);

        AssertOrdered(result, (left, right) => left.Rating >= right.Rating);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("LATEST")]
    public async Task FilterMoviesAsync_SortsLatestByYearThenIdDescending(string sortBy)
    {
        var result = await FilterAll(sortBy);

        AssertOrdered(result, (left, right) =>
            left.ReleaseYear > right.ReleaseYear ||
            left.ReleaseYear == right.ReleaseYear && left.Id >= right.Id);
    }

    [Theory]
    [InlineData("views")]
    [InlineData("VIEWS")]
    public async Task FilterMoviesAsync_SortsByViewsDescending(string sortBy)
    {
        var result = await FilterAll(sortBy);

        AssertOrdered(result, (left, right) => left.ViewCount >= right.ViewCount);
    }

    [Theory]
    [InlineData("trending")]
    [InlineData("")]
    [InlineData("unsupported-sort")]
    public async Task FilterMoviesAsync_DefaultSortPrioritizesTrendingThenRating(string sortBy)
    {
        var result = await FilterAll(sortBy);

        AssertOrdered(result, (left, right) =>
            left.IsTrending && !right.IsTrending ||
            left.IsTrending == right.IsTrending && left.Rating >= right.Rating);
    }

    [Fact]
    public async Task FilterMoviesAsync_ReturnsCorrectSecondPageMetadata()
    {
        var first = await _service.FilterMoviesAsync(new MovieFilterRequest { Page = 1, PageSize = 5 });
        var second = await _service.FilterMoviesAsync(new MovieFilterRequest { Page = 2, PageSize = 5 });

        Assert.Equal(5, second.Items.Count);
        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, second.TotalItems);
        Assert.Equal(2, second.Page);
        Assert.Equal(5, second.PageSize);
        Assert.Equal(8, second.TotalPages);
        Assert.True(second.HasNextPage);
        Assert.True(second.HasPreviousPage);
        Assert.Empty(first.Items.Select(movie => movie.Id).Intersect(second.Items.Select(movie => movie.Id)));
    }

    [Fact]
    public async Task FilterMoviesAsync_LastPageHasNoNextPage()
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest { Page = 8, PageSize = 5 });

        Assert.Equal(5, result.Items.Count);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public async Task FilterMoviesAsync_PagePastEndReturnsEmptyWithPreservedMetadata()
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest { Page = 99, PageSize = 5 });

        Assert.Empty(result.Items);
        Assert.Equal(DatabaseSeeder.CatalogMovies.Count, result.TotalItems);
        Assert.Equal(99, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FilterMoviesAsync_NonPositivePageFallsBackToFirstPage(int page)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest { Page = page, PageSize = 4 });

        Assert.Equal(1, result.Page);
        Assert.Equal(4, result.Items.Count);
        Assert.False(result.HasPreviousPage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FilterMoviesAsync_NonPositivePageSizeFallsBackToEighteen(int pageSize)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest { PageSize = pageSize });

        Assert.Equal(18, result.PageSize);
        Assert.Equal(18, result.Items.Count);
    }

    [Fact]
    public async Task GetSimilarMoviesAsync_WithUnknownMovieReturnsEmpty()
    {
        Assert.Empty(await _service.GetSimilarMoviesAsync(9999, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetSimilarMoviesAsync_WithNonPositiveCountReturnsEmpty(int count)
    {
        Assert.Empty(await _service.GetSimilarMoviesAsync(1, count));
    }

    [Fact]
    public async Task GetSimilarMoviesAsync_ExcludesCurrentMovieAndPrefersPlayableSimilarTitles()
    {
        const int movieId = 1;
        var current = Assert.Single(DatabaseSeeder.CatalogMovies, movie => movie.Id == movieId);

        var result = await _service.GetSimilarMoviesAsync(movieId, 100);

        Assert.DoesNotContain(result, movie => movie.Id == movieId);
        Assert.All(result, movie => Assert.True(
            movie.Type == current.Type || movie.Genres.Any(current.Genres.Contains)));

        // Chỉ gợi ý phim đã có tập phát — bấm vào phim chưa có nguồn thì người xem sẽ gặp
        // ngay màn hình "chưa có nguồn phát".
        Assert.All(result, movie => Assert.True(movie.TotalEpisodes > 0,
            $"Phim '{movie.Title}' được gợi ý nhưng chưa có tập phát."));

        // Thứ tự ưu tiên mức tương đồng (số thể loại chung) trước; trong cùng nhóm thì điểm
        // đánh giá cao hơn đứng trước.
        var sharedGenres = result
            .Select(movie => movie.Genres.Count(genre => current.Genres.Contains(genre)))
            .ToList();

        for (var index = 0; index < result.Count - 1; index++)
        {
            if (sharedGenres[index] > sharedGenres[index + 1]) continue;

            Assert.True(result[index].Rating >= result[index + 1].Rating,
                $"Phim tại vị trí {index} và {index + 1} không đúng thứ tự điểm đánh giá trong cùng nhóm tương đồng.");
        }
    }

    private async Task<List<WebXemPhim.Shared.Models.MovieDto>> FilterAll(string sortBy)
    {
        var result = await _service.FilterMoviesAsync(new MovieFilterRequest
        {
            SortBy = sortBy,
            PageSize = 100
        });
        return result.Items;
    }

    private static void AssertOrdered<T>(IReadOnlyList<T> items, Func<T, T, bool> predicate)
    {
        for (var index = 0; index < items.Count - 1; index++)
        {
            Assert.True(predicate(items[index], items[index + 1]),
                $"Items at indexes {index} and {index + 1} are not in the expected order.");
        }
    }
}
