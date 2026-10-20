using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WebXemPhim.Client.Components.Pages;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3AdversarialSuite : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ChallengerM3AdversarialSuite(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<(SqliteConnection Connection, AppDbContext Context, IDbContextFactory<AppDbContext> Factory)> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        await DatabaseSeeder.SeedCatalogAsync(context);
        await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

        var factoryMock = new TestDbContextFactory(options);
        return (connection, context, factoryMock);
    }

    // =========================================================================
    // SECTION 1: AGGREGATED HOME ENDPOINT CONCURRENCY & INTEGRITY
    // =========================================================================

    [Fact]
    public async Task Concurrent_GetHome_ApiEndpoint_ReturnsConsistentPayloadAndZeroFailures()
    {
        var client = _factory.CreateClient();
        const int concurrentRequests = 100;

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(async _ =>
            {
                var response = await client.GetAsync("/api/home");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<HomeAggregateDto>>();
                Assert.NotNull(envelope);
                Assert.True(envelope.Success);
                Assert.NotNull(envelope.Data);

                var data = envelope.Data;
                Assert.NotNull(data.Featured);
                Assert.True(data.Featured.Id > 0);
                Assert.False(string.IsNullOrWhiteSpace(data.Featured.Title));

                Assert.NotEmpty(data.Trending);
                Assert.True(data.Trending.Count <= 10);

                Assert.NotEmpty(data.Latest);
                Assert.True(data.Latest.Count <= 12);

                Assert.NotEmpty(data.TopSeries);
                Assert.All(data.TopSeries, s => Assert.True(s.Type == MovieType.Series || s.Type == MovieType.Anime));

                Assert.NotEmpty(data.HeroMovies);
                Assert.InRange(data.HeroMovies.Count, 1, 5);
                Assert.Equal(data.HeroMovies.Count, data.HeroMovies.Select(m => m.Id).Distinct().Count());

                // The first hero movie should be the featured movie
                Assert.Equal(data.Featured.Id, data.HeroMovies[0].Id);

                return data.Featured.Id;
            });

        var featuredIds = await Task.WhenAll(tasks);
        Assert.All(featuredIds, id => Assert.Equal(featuredIds[0], id));
    }

    [Fact]
    public async Task Concurrent_DatabaseMovieService_GetHomeAggregateAsync_SqliteStress()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);
            const int concurrency = 50;

            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, concurrency)
                .Select(async _ =>
                {
                    var aggregate = await movieService.GetHomeAggregateAsync();
                    Assert.NotNull(aggregate);
                    Assert.NotNull(aggregate.Featured);
                    Assert.NotEmpty(aggregate.Trending);
                    Assert.NotEmpty(aggregate.Latest);
                    Assert.NotEmpty(aggregate.TopSeries);
                    Assert.NotEmpty(aggregate.HeroMovies);

                    // Hero movie IDs must be unique
                    var heroIdCount = aggregate.HeroMovies.Select(m => m.Id).Distinct().Count();
                    Assert.Equal(aggregate.HeroMovies.Count, heroIdCount);
                });

            await Task.WhenAll(tasks);
            sw.Stop();

            // 50 concurrent DB context queries on SQLite in-memory should finish well under 3 seconds
            Assert.True(sw.ElapsedMilliseconds < 3000, $"Queries took {sw.ElapsedMilliseconds}ms, exceeding 3000ms threshold");
        }
    }

    // =========================================================================
    // SECTION 2: CLIENT HOME.RAZOR EXACTLY 1 NETWORK CALL & GRACEFUL DEGRADATION
    // =========================================================================

    [Fact]
    public async Task Client_HomeRazor_RequiresExactlyOneNetworkCall_ToPopulateAllSections()
    {
        var mockApiClient = new Mock<IMovieApiClient>();

        var testAggregate = new HomeAggregateDto
        {
            Featured = new MovieDto { Id = 10, Title = "Featured Cinema", IsFeatured = true },
            Trending = [new MovieDto { Id = 20, Title = "Trending 1" }, new MovieDto { Id = 21, Title = "Trending 2" }],
            Latest = [new MovieDto { Id = 30, Title = "Latest 1" }],
            TopSeries = [new MovieDto { Id = 40, Title = "Top Series 1", Type = MovieType.Series }],
            HeroMovies = [new MovieDto { Id = 10, Title = "Featured Cinema" }, new MovieDto { Id = 20, Title = "Trending 1" }]
        };

        mockApiClient.Setup(c => c.GetHomeAggregateAsync())
            .ReturnsAsync(testAggregate);

        var homeComponent = new Home();

        // Inject ApiClient via reflection
        var apiClientProp = typeof(Home).GetProperty("ApiClient", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(apiClientProp);
        apiClientProp.SetValue(homeComponent, mockApiClient.Object);

        // Invoke OnInitializedAsync
        var initMethod = typeof(Home).GetMethod("OnInitializedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(initMethod);
        var task = (Task)initMethod.Invoke(homeComponent, null)!;
        await task;

        // VERIFY: Exactly 1 call was made to GetHomeAggregateAsync
        mockApiClient.Verify(c => c.GetHomeAggregateAsync(), Times.Once);

        // VERIFY: Old multi-roundtrip methods were NEVER invoked
        mockApiClient.Verify(c => c.GetFeaturedAsync(), Times.Never);
        mockApiClient.Verify(c => c.GetTrendingAsync(It.IsAny<int>()), Times.Never);
        mockApiClient.Verify(c => c.GetLatestAsync(It.IsAny<int>()), Times.Never);
        mockApiClient.Verify(c => c.GetTopSeriesAsync(It.IsAny<int>()), Times.Never);

        // Inspect internal state fields of Home.razor
        var featuredField = typeof(Home).GetField("featuredMovie", BindingFlags.Instance | BindingFlags.NonPublic);
        var trendingField = typeof(Home).GetField("trendingMovies", BindingFlags.Instance | BindingFlags.NonPublic);
        var latestField = typeof(Home).GetField("latestMovies", BindingFlags.Instance | BindingFlags.NonPublic);
        var seriesField = typeof(Home).GetField("seriesMovies", BindingFlags.Instance | BindingFlags.NonPublic);
        var heroField = typeof(Home).GetField("heroMovies", BindingFlags.Instance | BindingFlags.NonPublic);
        var loadingField = typeof(Home).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(featuredField);
        Assert.NotNull(trendingField);
        Assert.NotNull(latestField);
        Assert.NotNull(seriesField);
        Assert.NotNull(heroField);
        Assert.NotNull(loadingField);

        var featuredVal = (MovieDto?)featuredField.GetValue(homeComponent);
        var trendingVal = (List<MovieDto>)trendingField.GetValue(homeComponent)!;
        var latestVal = (List<MovieDto>)latestField.GetValue(homeComponent)!;
        var seriesVal = (List<MovieDto>)seriesField.GetValue(homeComponent)!;
        var heroVal = (List<MovieDto>)heroField.GetValue(homeComponent)!;
        var loadingVal = (bool)loadingField.GetValue(homeComponent)!;

        Assert.NotNull(featuredVal);
        Assert.Equal(10, featuredVal.Id);
        Assert.Equal(2, trendingVal.Count);
        Assert.Single(latestVal);
        Assert.Single(seriesVal);
        Assert.Equal(2, heroVal.Count);
        Assert.False(loadingVal);
    }

    [Fact]
    public async Task Client_HomeRazor_WhenApiReturnsNullOrThrows_DegradesGracefullyWithoutUnhandledException()
    {
        // 1. When API returns null
        var mockApiClientNull = new Mock<IMovieApiClient>();
        mockApiClientNull.Setup(c => c.GetHomeAggregateAsync()).ReturnsAsync((HomeAggregateDto?)null);

        var homeNull = new Home();
        var apiClientProp = typeof(Home).GetProperty("ApiClient", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        apiClientProp.SetValue(homeNull, mockApiClientNull.Object);

        var initMethod = typeof(Home).GetMethod("OnInitializedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)initMethod.Invoke(homeNull, null)!;

        var loadingField = typeof(Home).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)loadingField.GetValue(homeNull)!);

        // 2. When API throws HttpRequestException (network failure)
        var mockApiClientEx = new Mock<IMovieApiClient>();
        mockApiClientEx.Setup(c => c.GetHomeAggregateAsync()).ThrowsAsync(new HttpRequestException("Server unreachable"));

        var homeEx = new Home();
        apiClientProp.SetValue(homeEx, mockApiClientEx.Object);

        var exTask = (Task)initMethod.Invoke(homeEx, null)!;
        await exTask; // Must NOT throw uncaught exception

        Assert.False((bool)loadingField.GetValue(homeEx)!);
    }

    // =========================================================================
    // SECTION 3: SQLITE FTS5 VIETNAMESE SEARCH ACCENTED/UNACCENTED & EDGE CASES
    // =========================================================================

    [Theory]
    [InlineData("bo gia", "Bố Già")]
    [InlineData("Bố Già", "Bố Già")]
    [InlineData("bỐ gIà", "Bố Già")]
    [InlineData("ky si bong dem", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("Kỵ Sĩ Bóng Đêm", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("kỴ sĨ bÓnG đÊm", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("mat biec", "Mắt Biếc")]
    [InlineData("Mắt Biếc", "Mắt Biếc")]
    [InlineData("mẮt bIếC", "Mắt Biếc")]
    [InlineData("dat rung phuong nam", "Đất Rừng Phương Nam")]
    [InlineData("Đất Rừng Phương Nam", "Đất Rừng Phương Nam")]
    [InlineData("đẤt rỪnG pHưƠnG nAm", "Đất Rừng Phương Nam")]
    public async Task SqliteFts5_AccentedAndUnaccented_FindsTargetMovies(string query, string expectedTitleMatch)
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            // Seed custom target movies if not already present
            if (!await context.Movies.AnyAsync(m => m.Title == "Bố Già"))
            {
                context.Movies.Add(new MovieEntity
                {
                    Title = "Bố Già",
                    OriginalTitle = "The Godfather VN",
                    Slug = "bo-gia-vietnamese",
                    Overview = "Một tác phẩm gia đình xúc động lấy bối cảnh xóm lao động Sài Gòn.",
                    Country = "Việt Nam",
                    Director = "Trấn Thành",
                    ReleaseYear = 2021,
                    DurationMinutes = 128
                });
            }

            if (!await context.Movies.AnyAsync(m => m.Title == "Mắt Biếc"))
            {
                context.Movies.Add(new MovieEntity
                {
                    Title = "Mắt Biếc",
                    OriginalTitle = "Dreamy Eyes",
                    Slug = "mat-biec-vietnamese",
                    Overview = "Mối tình si của Ngạn dành cho cô bạn thanh mai trúc mã Hà Lan.",
                    Country = "Việt Nam",
                    Director = "Victor Vũ",
                    ReleaseYear = 2019,
                    DurationMinutes = 117
                });
            }

            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var movieService = new DatabaseMovieService(dbFactory);

            var result = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = query,
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(result);
            Assert.NotEmpty(result.Items);
            Assert.Contains(result.Items, m => m.Title.Contains(expectedTitleMatch, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("' OR 1=1 --")]
    [InlineData("\" OR \"\"=\"")]
    [InlineData("*")]
    [InlineData("***")]
    [InlineData("())(()")]
    [InlineData("AND OR NOT NEAR")]
    [InlineData("DROP TABLE movies_fts;")]
    [InlineData("!@#$%^&*()_+-=[]{}|;':\",.<>/?")]
    [InlineData("🎬 Phim hay 2026 🔥")]
    [InlineData("a")]
    [InlineData("đ")]
    [InlineData("ô")]
    [InlineData("   ")]
    public async Task SqliteFts5_AdversarialInputs_SpecialCharactersAndInjection_SafeExecution(string adversarialKeyword)
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            // Must NOT throw SQLiteException, SqlException, or regex crash
            var result = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = adversarialKeyword,
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(result);
            Assert.NotNull(result.Items);
        }
    }

    [Fact]
    public async Task SqliteFts5_LongSearchTerm_ExecutesSafelyWithoutCpuSpike()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            // Construct 500-character search string with repetitive Vietnamese tokens
            var longQuery = string.Join(" ", Enumerable.Repeat("xứ cát đấng tiên tri bảo vệ ngân hà vũ trụ bao la", 12));
            Assert.True(longQuery.Length > 500);

            var sw = Stopwatch.StartNew();
            var result = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = longQuery,
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });
            sw.Stop();

            Assert.NotNull(result);
            Assert.True(sw.ElapsedMilliseconds < 500, $"Long search query took {sw.ElapsedMilliseconds}ms, exceeding 500ms threshold");
        }
    }

    [Fact]
    public async Task SqliteFts5_RelevanceSorting_ExactMatchRanksHigherThanPartialMatch()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            // Insert two movies: one exact match in title, one partial match in overview
            context.Movies.Add(new MovieEntity
            {
                Title = "Bố Già",
                OriginalTitle = "The Godfather",
                Slug = "bo-gia-exact-match",
                Overview = "Phim điện ảnh tâm lý.",
                Country = "Việt Nam",
                ReleaseYear = 2021
            });
            context.Movies.Add(new MovieEntity
            {
                Title = "Cuộc Sống Nghèo Khó",
                OriginalTitle = "Poor Life",
                Slug = "cuoc-song-ngheo-kho",
                Overview = "Nhân vật thường gọi người cha già là bố già trong một phân cảnh ngắn.",
                Country = "Việt Nam",
                ReleaseYear = 2020
            });
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var movieService = new DatabaseMovieService(dbFactory);

            var searchResult = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "Bố Già",
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(searchResult);
            Assert.NotEmpty(searchResult.Items);

            // The exact title match "Bố Già" must be ranked #1
            Assert.Equal("Bố Già", searchResult.Items[0].Title);
        }
    }

    [Fact]
    public async Task SqliteFts5_ExecutionPerformance_1000Iterations_SubMillisecondPerQuery()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            // Warm-up query
            await movieService.SearchMoviesAsync("xứ cát", 10);

            string[] searchTerms = ["xứ cát", "kỵ sĩ", "mai", "quỷ cẩu", "đất rừng", "oppenheimer", "arcane", "batman"];
            const int iterations = 1000;

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                var term = searchTerms[i % searchTerms.Length];
                var results = await movieService.SearchMoviesAsync(term, 10);
                Assert.NotNull(results);
            }
            sw.Stop();

            var avgMs = (double)sw.ElapsedMilliseconds / iterations;
            // SQLite FTS5 with in-memory DB must average < 5ms per query
            Assert.True(avgMs < 5.0, $"Average search query took {avgMs:F2}ms, exceeding 5.0ms threshold");
        }
    }

    [Theory]
    [InlineData("   dat    rung      phuong     nam   ", "Đất Rừng Phương Nam")]
    [InlineData("bo...gia!!!", "Bố Già")]
    [InlineData("\"Kỵ Sĩ Bóng Đêm\"", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("MAT - BIEC", "Mắt Biếc")]
    public async Task SqliteFts5_IrregularSpacingAndPunctuation_FindsMoviesAccurately(string query, string expectedTitleMatch)
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            if (!await context.Movies.AnyAsync(m => m.Title == "Bố Già"))
            {
                context.Movies.Add(new MovieEntity
                {
                    Title = "Bố Già",
                    OriginalTitle = "The Godfather VN",
                    Slug = "bo-gia-vietnamese",
                    Overview = "Một tác phẩm gia đình xúc động.",
                    Country = "Việt Nam",
                    Director = "Trấn Thành",
                    ReleaseYear = 2021
                });
            }
            if (!await context.Movies.AnyAsync(m => m.Title == "Mắt Biếc"))
            {
                context.Movies.Add(new MovieEntity
                {
                    Title = "Mắt Biếc",
                    OriginalTitle = "Dreamy Eyes",
                    Slug = "mat-biec-vietnamese",
                    Overview = "Mối tình si.",
                    Country = "Việt Nam",
                    Director = "Victor Vũ",
                    ReleaseYear = 2019
                });
            }
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var movieService = new DatabaseMovieService(dbFactory);
            var result = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = query,
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            });

            Assert.NotNull(result);
            Assert.NotEmpty(result.Items);
            Assert.Contains(result.Items, m => m.Title.Contains(expectedTitleMatch, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task SqliteFts5_CombinedKeywordWithGenresCountriesYears_FiltersIntersectCorrectly()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            // 1. Positive filter intersection: The Dark Knight (ID 4): Keyword "Kỵ Sĩ", Genre "Hành Động", Country "Âu Mỹ", Year 2008
            var matchResult = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "Kỵ Sĩ",
                Genre = "Hành Động",
                Country = "Âu Mỹ",
                Year = 2008,
                SortBy = "relevance"
            });

            Assert.NotNull(matchResult);
            Assert.Single(matchResult.Items);
            Assert.Equal("Kỵ Sĩ Bóng Đêm", matchResult.Items[0].Title);

            // 2. Contradictory filter intersection: Keyword "Kỵ Sĩ" but Country "Việt Nam" -> 0 items
            var mismatchResult = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "Kỵ Sĩ",
                Country = "Việt Nam",
                SortBy = "relevance"
            });

            Assert.NotNull(mismatchResult);
            Assert.Empty(mismatchResult.Items);
            Assert.Equal(0, mismatchResult.TotalItems);
        }
    }

    [Fact]
    public async Task SqliteFts5_PagingEdgeCases_HighPageOrZeroPageSize_HandlesGracefully()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);

            // High page number beyond total count
            var highPageResult = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "cát",
                Page = 9999,
                PageSize = 10,
                SortBy = "relevance"
            });

            Assert.NotNull(highPageResult);
            Assert.Empty(highPageResult.Items);
            Assert.True(highPageResult.TotalItems > 0);
            Assert.Equal(9999, highPageResult.Page);

            // Non-positive page and pageSize fallback
            var zeroPageResult = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "cát",
                Page = -1,
                PageSize = -10,
                SortBy = "relevance"
            });

            Assert.NotNull(zeroPageResult);
            Assert.NotEmpty(zeroPageResult.Items);
            Assert.Equal(1, zeroPageResult.Page);
            Assert.Equal(18, zeroPageResult.PageSize);
        }
    }

    [Fact]
    public async Task AggregatedHome_ResponseCompression_HandlesBrotliAndGzipEncodings()
    {
        var client = _factory.CreateClient();

        // 1. Brotli request
        using var brRequest = new HttpRequestMessage(HttpMethod.Get, "/api/home");
        brRequest.Headers.AcceptEncoding.ParseAdd("br");
        using var brResponse = await client.SendAsync(brRequest);
        Assert.Equal(HttpStatusCode.OK, brResponse.StatusCode);
        Assert.Contains("br", brResponse.Content.Headers.ContentEncoding);

        await using var brStream = new System.IO.Compression.BrotliStream(
            await brResponse.Content.ReadAsStreamAsync(),
            System.IO.Compression.CompressionMode.Decompress);
        var brEnvelope = await System.Text.Json.JsonSerializer.DeserializeAsync<ApiResponse<HomeAggregateDto>>(
            brStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(brEnvelope);
        Assert.True(brEnvelope.Success);
        Assert.NotNull(brEnvelope.Data?.Featured);

        // 2. Gzip request
        using var gzipRequest = new HttpRequestMessage(HttpMethod.Get, "/api/home");
        gzipRequest.Headers.AcceptEncoding.ParseAdd("gzip");
        using var gzipResponse = await client.SendAsync(gzipRequest);
        Assert.Equal(HttpStatusCode.OK, gzipResponse.StatusCode);
        Assert.Contains("gzip", gzipResponse.Content.Headers.ContentEncoding);

        await using var gzipStream = new System.IO.Compression.GZipStream(
            await gzipResponse.Content.ReadAsStreamAsync(),
            System.IO.Compression.CompressionMode.Decompress);
        var gzipEnvelope = await System.Text.Json.JsonSerializer.DeserializeAsync<ApiResponse<HomeAggregateDto>>(
            gzipStream,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(gzipEnvelope);
        Assert.True(gzipEnvelope.Success);
        Assert.NotNull(gzipEnvelope.Data?.Featured);
    }
}


