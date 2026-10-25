using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class BackendPerformanceAndCachingTests : IClassFixture<TestApiFactory>, IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly TestApiFactory _factory;
    private readonly PostgresTestDatabaseFixture _fixture;

    public BackendPerformanceAndCachingTests(TestApiFactory factory, PostgresTestDatabaseFixture fixture)
    {
        _factory = factory;
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    /// <summary>
    /// Mở connection PostgreSQL thật, nạp lại catalog mẫu và đồng bộ index tìm kiếm +
    /// identity sequence để các controller sinh id tự động không đụng id đã seed.
    /// </summary>
    private async Task<(NpgsqlConnection Connection, AppDbContext Context, IDbContextFactory<AppDbContext> Factory)> CreatePostgresContextAsync()
    {
        var (connection, context, dbFactory) = await Database.CreateOpenContextAsync();

        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        await DatabaseSeeder.SeedCatalogAsync(context);
        await DatabaseSchemaHelper.SyncMovieSearchIndexAsync(context);
        await DatabaseSchemaHelper.SyncIdentitySequencesAsync(context);

        return (connection, context, dbFactory);
    }

    [Fact]
    public void ResponseCompression_Configured_WithBrotliGzipAndFastestLevel()
    {
        using var scope = _factory.Services.CreateScope();
        var compressionOptions = scope.ServiceProvider.GetRequiredService<IOptions<ResponseCompressionOptions>>().Value;
        var brotliOptions = scope.ServiceProvider.GetRequiredService<IOptions<BrotliCompressionProviderOptions>>().Value;
        var gzipOptions = scope.ServiceProvider.GetRequiredService<IOptions<GzipCompressionProviderOptions>>().Value;

        Assert.True(compressionOptions.EnableForHttps);
        Assert.Contains("application/json", compressionOptions.MimeTypes);
        Assert.Contains("application/problem+json", compressionOptions.MimeTypes);
        Assert.Equal(CompressionLevel.Fastest, brotliOptions.Level);
        Assert.Equal(CompressionLevel.Fastest, gzipOptions.Level);
    }

    [Fact]
    public void OutputCache_CatalogEndpoints_HaveOutputCacheAttributesConfigured()
    {
        // 1. HomeController.GetHome -> [OutputCache(Duration = 60, Tags = ["movies"])]
        var homeMethod = typeof(HomeController).GetMethod(nameof(HomeController.GetHome));
        Assert.NotNull(homeMethod);
        var homeAttr = homeMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(homeAttr);
        Assert.Equal(60, homeAttr.Duration);
        Assert.NotNull(homeAttr.Tags);
        Assert.Contains("movies", homeAttr.Tags);

        // 2. MoviesController endpoints
        var filterOptionsMethod = typeof(MoviesController).GetMethod(nameof(MoviesController.FilterOptions));
        Assert.NotNull(filterOptionsMethod);
        var filterAttr = filterOptionsMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(filterAttr);
        Assert.Equal(120, filterAttr.Duration);
        Assert.NotNull(filterAttr.Tags);
        Assert.Contains("movies", filterAttr.Tags);
        Assert.Contains("genres", filterAttr.Tags);

        var featuredMethod = typeof(MoviesController).GetMethod(nameof(MoviesController.GetFeatured));
        Assert.NotNull(featuredMethod);
        var featuredAttr = featuredMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(featuredAttr);
        Assert.Equal(60, featuredAttr.Duration);
        Assert.NotNull(featuredAttr.Tags);
        Assert.Contains("movies", featuredAttr.Tags);

        var trendingMethod = typeof(MoviesController).GetMethod(nameof(MoviesController.GetTrending));
        Assert.NotNull(trendingMethod);
        var trendingAttr = trendingMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(trendingAttr);
        Assert.Equal(60, trendingAttr.Duration);
        Assert.NotNull(trendingAttr.Tags);
        Assert.Contains("movies", trendingAttr.Tags);

        var topSeriesMethod = typeof(MoviesController).GetMethod(nameof(MoviesController.GetTopSeries));
        Assert.NotNull(topSeriesMethod);
        var topSeriesAttr = topSeriesMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(topSeriesAttr);
        Assert.Equal(60, topSeriesAttr.Duration);
        Assert.NotNull(topSeriesAttr.Tags);
        Assert.Contains("movies", topSeriesAttr.Tags);

        var latestMethod = typeof(MoviesController).GetMethod(nameof(MoviesController.GetLatest));
        Assert.NotNull(latestMethod);
        var latestAttr = latestMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(latestAttr);
        Assert.Equal(60, latestAttr.Duration);
        Assert.NotNull(latestAttr.Tags);
        Assert.Contains("movies", latestAttr.Tags);

        // 3. GenresController.GetAll -> [OutputCache(Duration = 120, Tags = ["genres"])]
        var genresMethod = typeof(GenresController).GetMethod(nameof(GenresController.GetAll));
        Assert.NotNull(genresMethod);
        var genresAttr = genresMethod.GetCustomAttribute<OutputCacheAttribute>();
        Assert.NotNull(genresAttr);
        Assert.Equal(120, genresAttr.Duration);
        Assert.NotNull(genresAttr.Tags);
        Assert.Contains("genres", genresAttr.Tags);
    }

    [Fact]
    public async Task AdminMoviesController_Mutations_EvictOutputCacheTags()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            var mockCacheStore = new Mock<IOutputCacheStore>();
            var controller = new AdminMoviesController(
                dbFactory,
                TimeProvider.System,
                outputCacheStore: mockCacheStore.Object);

            // 1. Create Movie
            var createReq = new UpsertMovieRequest
            {
                Title = "Phim Kiểm Thử Caching M3",
                ReleaseYear = 2026,
                DurationMinutes = 110,
                Type = MovieType.Movie
            };
            var createRes = await controller.CreateMovie(createReq);
            Assert.IsType<OkObjectResult>(createRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());

            var createdId = ((createRes.Result as OkObjectResult)!.Value as ApiResponse<MovieDetailDto>)!.Data!.Id;

            // 2. Update Movie
            mockCacheStore.Reset();
            var updateReq = new UpsertMovieRequest
            {
                Title = "Phim Kiểm Thử Caching M3 Đã Cập Nhật",
                ReleaseYear = 2026,
                DurationMinutes = 115,
                Type = MovieType.Movie
            };
            var updateRes = await controller.UpdateMovie(createdId, updateReq);
            Assert.IsType<OkObjectResult>(updateRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());

            // 3. Bulk Action
            mockCacheStore.Reset();
            var bulkReq = new BulkMovieActionRequest
            {
                Action = "set_featured",
                MovieIds = [createdId]
            };
            var bulkRes = await controller.BulkMovieAction(bulkReq);
            Assert.IsType<OkObjectResult>(bulkRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());

            // 4. Clone Movie
            mockCacheStore.Reset();
            var cloneReq = new CloneMovieRequest
            {
                NewTitle = "Phim Nhân Bản Caching M3",
                ReleaseYear = 2026
            };
            var cloneRes = await controller.CloneMovie(createdId, cloneReq);
            Assert.IsType<OkObjectResult>(cloneRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());

            // 5. Delete Movie
            mockCacheStore.Reset();
            var deleteRes = await controller.DeleteMovie(createdId);
            Assert.IsType<OkObjectResult>(deleteRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }

    [Fact]
    public async Task AdminGenresController_Mutations_EvictOutputCacheTags()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            var mockCacheStore = new Mock<IOutputCacheStore>();
            var controller = new AdminGenresController(
                dbFactory,
                TimeProvider.System,
                outputCacheStore: mockCacheStore.Object);

            // 1. Get Genres
            var getRes = await controller.GetGenres();
            Assert.IsType<OkObjectResult>(getRes.Result);

            // 2. Create Genre
            var createReq = new UpsertGenreRequest { Name = "Thể Loại Test M3", Slug = "the-loai-test-m3" };
            var createRes = await controller.CreateGenre(createReq);
            Assert.IsType<OkObjectResult>(createRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.Once());
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.Once());

            var genreId = ((createRes.Result as OkObjectResult)!.Value as ApiResponse<GenreDto>)!.Data!.Id;

            // 3. Update Genre
            mockCacheStore.Reset();
            var updateReq = new UpsertGenreRequest { Name = "Thể Loại Test M3 Đã Đổi Tên", Slug = "the-loai-test-m3-doi-ten" };
            var updateRes = await controller.UpdateGenre(genreId, updateReq);
            Assert.IsType<OkObjectResult>(updateRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.Once());
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.Once());

            // 4. Delete Genre
            mockCacheStore.Reset();
            var deleteRes = await controller.DeleteGenre(genreId);
            Assert.IsType<OkObjectResult>(deleteRes.Result);
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.Once());
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.Once());
        }
    }

    [Fact]
    public async Task DatabaseMovieService_GetHomeAggregateAsync_ReturnsCompletePopulatedSections()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            var aggregate = await movieService.GetHomeAggregateAsync();

            Assert.NotNull(aggregate);
            Assert.NotNull(aggregate.Featured);
            Assert.True(aggregate.Featured.IsFeatured);
            Assert.NotEmpty(aggregate.Trending);
            Assert.NotEmpty(aggregate.Latest);
            Assert.NotEmpty(aggregate.TopSeries);
            Assert.NotEmpty(aggregate.HeroMovies);

            // Hero movies contains at most 5 items and unique IDs
            Assert.InRange(aggregate.HeroMovies.Count, 1, 5);
            Assert.Equal(aggregate.HeroMovies.Count, aggregate.HeroMovies.Select(m => m.Id).Distinct().Count());
        }
    }

    [Fact]
    public async Task HomeController_GetHome_ReturnsSuccessEnvelope()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);
            var controller = new HomeController(movieService);

            var result = await controller.GetHome();
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var envelope = Assert.IsType<ApiResponse<HomeAggregateDto>>(okResult.Value);

            Assert.True(envelope.Success);
            Assert.NotNull(envelope.Data);
            Assert.NotNull(envelope.Data.Featured);
            Assert.NotEmpty(envelope.Data.Trending);
        }
    }

    [Fact]
    public async Task MovieApiClient_GetHomeAggregateAsync_ParsesPayloadSuccessfully()
    {
        var aggregatePayload = new HomeAggregateDto
        {
            Featured = new MovieDto { Id = 1, Title = "Dune Test" },
            Trending = [new MovieDto { Id = 2, Title = "Oppenheimer Test" }],
            Latest = [new MovieDto { Id = 3, Title = "Avatar Test" }],
            TopSeries = [new MovieDto { Id = 4, Title = "Breaking Bad Test" }],
            HeroMovies = [new MovieDto { Id = 1, Title = "Dune Test" }]
        };

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, ApiResponse<HomeAggregateDto>.Ok(aggregatePayload));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var apiClient = new MovieApiClient(httpClient, NullLogger<MovieApiClient>.Instance);

        var result = await apiClient.GetHomeAggregateAsync();

        Assert.NotNull(result);
        Assert.Equal(1, result.Featured?.Id);
        Assert.Single(result.Trending);
        Assert.Equal("Oppenheimer Test", result.Trending[0].Title);
    }

    [Fact]
    public async Task MovieApiClient_GetHomeAggregateAsync_WhenNotFound_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, null);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var apiClient = new MovieApiClient(httpClient, NullLogger<MovieApiClient>.Instance);

        var result = await apiClient.GetHomeAggregateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task MovieApiClient_GetHomeAggregateAsync_WhenServerError_ReturnsNullWithoutException()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, null);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var apiClient = new MovieApiClient(httpClient, NullLogger<MovieApiClient>.Instance);

        var result = await apiClient.GetHomeAggregateAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task PostgreSqlFullTextSearch_RelevanceSorting_AndVietnameseSearch_Optimized()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            context.Movies.Add(new MovieEntity
            {
                Title = "Bố Già",
                OriginalTitle = "The Godfather VN",
                Slug = "bo-gia-test",
                Overview = "Phim Bố Già chiếu rạp.",
                Country = "Việt Nam",
                Director = "Trấn Thành",
                ReleaseYear = 2021,
                DurationMinutes = 128
            });
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMovieSearchIndexAsync(context);

            var movieService = new DatabaseMovieService(dbFactory);

            // Test unaccented Vietnamese search "nguoi nhen" -> finds "Người Nhện: Du Hành Vũ Trụ Nhện"
            var spiderManSearch = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "nguoi nhen",
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(spiderManSearch);
            Assert.NotEmpty(spiderManSearch.Items);
            Assert.Contains(spiderManSearch.Items, m => m.Title.Contains("Người Nhện"));

            // Test unaccented search "bo gia" -> finds "Bố Già"
            var godfatherSearch = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "bo gia",
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(godfatherSearch);
            Assert.NotEmpty(godfatherSearch.Items);
            Assert.Contains(godfatherSearch.Items, m => m.Title.Contains("Bố Già"));
        }
    }

    [Fact]
    public async Task DatabaseMovieService_GetFilterOptionsAsync_QueriesOptimizedGenresAndCountries()
    {
        var (connection, context, dbFactory) = await CreatePostgresContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            var options = await movieService.GetFilterOptionsAsync();

            Assert.NotNull(options);
            Assert.NotEmpty(options.Genres);
            Assert.NotEmpty(options.Countries);
            Assert.NotEmpty(options.Years);

            // Verify Genres contain catalog genres
            Assert.Contains("Hành Động", options.Genres);
            Assert.Contains("Việt Nam", options.Countries);
            // Verify Years are descending
            Assert.Equal(options.Years.OrderByDescending(y => y).ToList(), options.Years);
        }
    }

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, object? payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (payload != null)
            {
                response.Content = JsonContent.Create(payload);
            }
            return Task.FromResult(response);
        }
    }
}
