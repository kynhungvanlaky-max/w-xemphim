using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Milestone 5 Comprehensive Final Adversarial Stress Testing Suite.
/// Empirically challenges and stress-tests all 4 pillars of the project:
/// Pillar 1: R1 Security & Protection (Stream URL tampering, Webhook HMAC rejection, Rate limiter burst 429)
/// Pillar 2: R2 Data Integrity & Reliability (Non-existent slug 404/null verification, Atomic view counter concurrency, Same-IP deduplication)
/// Pillar 3: R3 Performance & Caching (OutputCache latency < 15ms, Immediate cache eviction, Single round-trip /api/home, SQLite FTS5 unaccented Vietnamese search)
/// Pillar 4: R4 Frontend Assets & Loading (wwwroot/images total size < 15MB, 0 PNGs > 500KB, 0 broken image links across Razor/CSS, App.razor dynamic scripts)
/// </summary>
public sealed class ChallengerM5FinalAdversarialTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ChallengerM5FinalAdversarialTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        if (File.Exists(Path.Combine(dir, "WebXemPhim.slnx")))
        {
            return dir;
        }

        dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "src", "Client")))
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return dir;
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
    // PILLAR 1: R1 ADVERSARIAL SECURITY & TAMPER RESISTANCE TESTS
    // =========================================================================

    [Fact]
    public async Task Pillar1_StreamUrl_TamperedToken_Returns403Forbidden()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = _factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var validExp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var validSig = signer.ComputeSignature(101, 1, validExp, ipHash);

        // Tamper signature by modifying the trailing characters
        var tamperedSig = validSig.EndsWith('0') ? validSig[..^1] + "1" : validSig[..^1] + "0";
        var tamperedUrl = $"/api/stream/play?episodeId=101&pos=1&exp={validExp}&ip={ipHash}&sig={tamperedSig}";

        var response = await client.GetAsync(tamperedUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        Assert.NotNull(payload);
        Assert.False(payload.Success);
    }

    [Fact]
    public async Task Pillar1_StreamUrl_ExpiredTimestamp_Returns403Forbidden()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = _factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var expiredExp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var validSigOnExpiredExp = signer.ComputeSignature(101, 1, expiredExp, ipHash);

        var expiredUrl = $"/api/stream/play?episodeId=101&pos=1&exp={expiredExp}&ip={ipHash}&sig={validSigOnExpiredExp}";

        var response = await client.GetAsync(expiredUrl);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        Assert.NotNull(payload);
        Assert.False(payload.Success);
    }

    [Fact]
    public async Task Pillar1_StreamUrl_MismatchedClientIp_Returns403Forbidden()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = _factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var validExp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        // Generate signature tied strictly to IP 192.168.1.100
        var boundIpHash = signer.ComputeIpHash("192.168.1.100");
        var sig = signer.ComputeSignature(101, 1, validExp, boundIpHash);

        // Request arrives from default test client (127.0.0.1), which mismatches the token's bound IP
        var url = $"/api/stream/play?episodeId=101&pos=1&exp={validExp}&ip={boundIpHash}&sig={sig}";
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pillar1_PaymentWebhook_InvalidSignature_Returns403Forbidden()
    {
        var client = _factory.CreateClient();

        var fraudulentWebhook = new PaymentWebhookRequest
        {
            OrderCode = "PAY-VIP-FRAUD-001",
            Amount = 150000,
            Status = "PAID",
            TransactionReference = "TX-FAKE-9999",
            Signature = "badc0de00112233445566778899aabbccddeeff00112233445566778899aabbcc"
        };

        var response = await client.PostAsJsonAsync("/api/payments/webhook", fraudulentWebhook);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PaymentDto>>();
        Assert.NotNull(payload);
        Assert.False(payload.Success);
        Assert.Contains("403", payload.Message);
    }

    [Fact]
    public async Task Pillar1_PaymentWebhook_TamperedPayload_Returns403Forbidden()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<IPaymentWebhookVerifier>();

        // Generate authentic signature for order with amount 79,000
        var legitimateReq = new PaymentWebhookRequest
        {
            OrderCode = "ORDER-LEGIT-777",
            Amount = 79000,
            Status = "PAID",
            TransactionReference = "TX-777"
        };
        var validSignature = verifier.ComputeSignature(legitimateReq);

        // Adversary alters the amount to 10,000 while retaining original signature
        var tamperedReq = new PaymentWebhookRequest
        {
            OrderCode = legitimateReq.OrderCode,
            Amount = 10000, // Tampered
            Status = legitimateReq.Status,
            TransactionReference = legitimateReq.TransactionReference,
            Signature = validSignature
        };

        var response = await client.PostAsJsonAsync("/api/payments/webhook", tamperedReq);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pillar1_RateLimiting_BurstAuthRequests_Returns429TooManyRequests()
    {
        // Setup isolated factory with tight Auth rate limit of 5 requests/minute
        using var throttledFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimits:AuthPerMinute"] = "5"
            }));
        });

        using var client = throttledFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.77, 10.0.0.1");

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = "victim@cinemaflow.test",
                Password = "WrongPassword!"
            });
            statusCodes.Add(res.StatusCode);
        }

        // Verify that the burst exceeding 5/min triggered HTTP 429 Too Many Requests
        Assert.Contains(HttpStatusCode.TooManyRequests, statusCodes);
        Assert.Equal(HttpStatusCode.TooManyRequests, statusCodes.Last());
    }

    // =========================================================================
    // PILLAR 2: R2 ADVERSARIAL DATA INTEGRITY & RELIABILITY TESTS
    // =========================================================================

    [Theory]
    [InlineData("non-existent-slug-999")]
    [InlineData("dune-3-fake-slug")]
    [InlineData("random-unseeded-slug-404")]
    public async Task Pillar2_MovieDetail_NonExistentSlugs_Returns404Null_AndNeverReturnsDuneOrPlyr(string slug)
    {
        var client = _factory.CreateClient();

        // 1. Server API endpoint directly returns 404
        var apiResponse = await client.GetAsync($"/api/movies/slug/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, apiResponse.StatusCode);

        // 2. Client service method returns null honestly
        var movieApiClient = new MovieApiClient(client, NullLogger<MovieApiClient>.Instance);
        var movieDetail = await movieApiClient.GetMovieBySlugAsync(slug);
        Assert.Null(movieDetail);

        // 3. Raw response body must never leak mock Dune 3 or plyr CDN stream
        var rawContent = await apiResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Dune: Part Two", rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn.plyr.io", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pillar2_AtomicViewCounter_MultiIpConcurrentRequests_ZeroLostUpdates()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var testMovie = new MovieEntity
            {
                Title = "Phim Đua View Đa IP M5",
                Slug = "phim-dua-view-da-ip-m5",
                ReleaseYear = 2026,
                DurationMinutes = 120,
                ViewCount = 100
            };
            context.Movies.Add(testMovie);
            await context.SaveChangesAsync();

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var movieService = new DatabaseMovieService(dbFactory, memoryCache);

            const int concurrentCallers = 50;
            var tasks = Enumerable.Range(1, concurrentCallers)
                .Select(i =>
                {
                    var clientIp = $"10.200.{i / 255}.{i % 255 + 1}";
                    return movieService.IncrementViewCountAsync(testMovie.Id, clientIp);
                });

            var results = await Task.WhenAll(tasks);
            Assert.All(results, success => Assert.True(success));

            // Verify the view count in SQLite reflects 100 + 50 = exactly 150 (zero lost updates)
            await using var freshContext = await dbFactory.CreateDbContextAsync();
            var updatedMovie = await freshContext.Movies.FindAsync(testMovie.Id);
            Assert.NotNull(updatedMovie);
            Assert.Equal(150, updatedMovie.ViewCount);
        }
    }

    [Fact]
    public async Task Pillar2_AtomicViewCounter_SameIpBurstRequests_DeduplicatesWithinCooldown()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var testMovie = new MovieEntity
            {
                Title = "Phim Burst Cùng IP M5",
                Slug = "phim-burst-cung-ip-m5",
                ReleaseYear = 2026,
                DurationMinutes = 105,
                ViewCount = 0
            };
            context.Movies.Add(testMovie);
            await context.SaveChangesAsync();

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var movieService = new DatabaseMovieService(dbFactory, memoryCache);

            const string sameIp = "172.16.88.99";
            const int burstRequests = 30;

            var tasks = Enumerable.Range(0, burstRequests)
                .Select(_ => movieService.IncrementViewCountAsync(testMovie.Id, sameIp));

            var results = await Task.WhenAll(tasks);
            Assert.All(results, success => Assert.True(success));

            // Verify view count only incremented by 1 due to 10-minute sliding cooldown
            await using var freshContext = await dbFactory.CreateDbContextAsync();
            var updatedMovie = await freshContext.Movies.FindAsync(testMovie.Id);
            Assert.NotNull(updatedMovie);
            Assert.Equal(1, updatedMovie.ViewCount);
        }
    }

    // =========================================================================
    // PILLAR 3: R3 ADVERSARIAL PERFORMANCE & CACHING TESTS
    // =========================================================================

    [Fact]
    public async Task Pillar3_OutputCache_PublicCatalogEndpoints_SubsequentRequestsUnder15ms()
    {
        var client = _factory.CreateClient();

        var endpoints = new[]
        {
            "/api/home",
            "/api/movies/featured",
            "/api/movies/filter-options",
            "/api/genres"
        };

        foreach (var endpoint in endpoints)
        {
            // Initial warm-up request
            var warmUp = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);

            // Measure 10 subsequent cached requests
            const int iterations = 10;
            var latencies = new List<double>(iterations);

            for (var i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                var cached = await client.GetAsync(endpoint);
                sw.Stop();

                Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgLatency = latencies.Average();
            Assert.True(avgLatency < 15.0, $"OutputCache hit latency for {endpoint} was {avgLatency:F2}ms, which exceeds target < 15ms.");
        }
    }

    [Fact]
    public async Task Pillar3_OutputCache_AdminCatalogUpdate_ImmediatelyEvictsCache()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var mockCacheStore = new Mock<IOutputCacheStore>();
            var adminMovies = new AdminMoviesController(
                dbFactory,
                TimeProvider.System,
                outputCacheStore: mockCacheStore.Object);

            var createReq = new UpsertMovieRequest
            {
                Title = "Phim Thử Nghiệm Eviction M5",
                ReleaseYear = 2026,
                DurationMinutes = 135,
                Type = MovieType.Movie
            };

            var createRes = await adminMovies.CreateMovie(createReq);
            Assert.IsType<OkObjectResult>(createRes.Result);

            // Assert that tags "movies" and "genres" were evicted immediately
            mockCacheStore.Verify(s => s.EvictByTagAsync("movies", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
            mockCacheStore.Verify(s => s.EvictByTagAsync("genres", It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        }
    }

    [Fact]
    public async Task Pillar3_AggregatedHomeEndpoint_SingleRoundTrip_PopulatesAllSections()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/home");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<HomeAggregateDto>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);

        var data = envelope.Data;
        Assert.NotNull(data);
        Assert.NotNull(data.Featured);
        Assert.NotEmpty(data.Trending);
        Assert.NotEmpty(data.Latest);
        Assert.NotEmpty(data.TopSeries);
        Assert.NotEmpty(data.HeroMovies);

        // Verify HeroMovies has unique IDs and contains Featured movie
        Assert.InRange(data.HeroMovies.Count, 1, 5);
        Assert.Equal(data.HeroMovies.Count, data.HeroMovies.Select(m => m.Id).Distinct().Count());
        Assert.Equal(data.Featured.Id, data.HeroMovies[0].Id);
    }

    [Fact]
    public async Task Pillar3_SqliteFts5_UnaccentedVietnameseSearch_EdgeCases()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            context.Movies.Add(new MovieEntity
            {
                Title = "Lật Mặt 7: Một Điều Ước",
                OriginalTitle = "Face Off 7",
                Slug = "lat-mat-7",
                Overview = "Phim điện ảnh gia đình xúc động của Lý Hải.",
                Country = "Việt Nam",
                Director = "Lý Hải",
                ReleaseYear = 2024,
                DurationMinutes = 138
            });
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var movieService = new DatabaseMovieService(dbFactory);

            // Edge Case 1: All lowercase unaccented
            var res1 = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "lat mat",
                SortBy = "relevance"
            });
            Assert.Contains(res1.Items, m => m.Title.Contains("Lật Mặt 7"));

            // Edge Case 2: ALL UPPERCASE with accents
            var res2 = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "LẬT MẶT",
                SortBy = "relevance"
            });
            Assert.Contains(res2.Items, m => m.Title.Contains("Lật Mặt 7"));

            // Edge Case 3: Director search unaccented
            var res3 = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "ly hai",
                SortBy = "relevance"
            });
            Assert.Contains(res3.Items, m => m.Title.Contains("Lật Mặt 7"));

            // Edge Case 4: Punctuation and quotes in keyword must not trigger SQLite FTS syntax errors
            var res4 = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "\"lat-mat\"*",
                SortBy = "relevance"
            });
            Assert.NotNull(res4);

            // Edge Case 5: Special characters edge case
            var res5 = await movieService.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "@@##$$%%^^",
                SortBy = "relevance"
            });
            Assert.NotNull(res5);
            Assert.Empty(res5.Items);
        }
    }

    // =========================================================================
    // PILLAR 4: R4 FRONTEND ASSETS & PERFORMANCE TESTS
    // =========================================================================

    [Fact]
    public void Pillar4_ImageDirectory_TotalSizeUnder15MB_AndZeroPngsExceed500KB()
    {
        var root = GetSolutionRoot();
        var imagesDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "wwwroot", "images");
        Assert.True(Directory.Exists(imagesDir), $"Directory not found: {imagesDir}");

        var files = Directory.GetFiles(imagesDir, "*.*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var totalMB = totalBytes / (1024.0 * 1024.0);

        // 1. Total size must be < 15MB
        Assert.True(totalMB < 15.0, $"Total wwwroot/images size ({totalMB:F2} MB) exceeds 15 MB limit.");

        // 2. 0 PNGs > 500KB
        var pngFiles = Directory.GetFiles(imagesDir, "*.png", SearchOption.AllDirectories);
        var oversizedPngs = pngFiles
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 500 * 1024)
            .ToList();

        Assert.Empty(oversizedPngs);
    }

    [Fact]
    public void Pillar4_StaticImageLinks_ZeroBroken404ReferencesAcrossAllRazorAndCss()
    {
        var root = GetSolutionRoot();
        var clientDir = Path.Combine(root, "src", "Client", "WebXemPhim.Client");
        var wwwrootDir = Path.Combine(clientDir, "wwwroot");

        var razorAndCssFiles = Directory.GetFiles(clientDir, "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(clientDir, "*.css", SearchOption.AllDirectories))
            .ToList();

        Assert.NotEmpty(razorAndCssFiles);

        var imageRegex = new Regex(@"(?:/images/|(?<=url\(['""]?))(?:/images/|images/)([^'""\)\s;>]+?\.(?:webp|png|jpg|jpeg|svg|avif))", RegexOptions.IgnoreCase);
        var directPathRegex = new Regex(@"/images/([a-zA-Z0-9_\-\./]+?\.(?:webp|png|jpg|jpeg|svg|avif))", RegexOptions.IgnoreCase);

        var brokenLinks = new List<string>();
        var checkedCount = 0;

        foreach (var file in razorAndCssFiles)
        {
            var content = File.ReadAllText(file);

            var matches = directPathRegex.Matches(content);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var relPath = match.Groups[1].Value.Trim();
                var fullPath = Path.Combine(wwwrootDir, "images", relPath.Replace('/', Path.DirectorySeparatorChar));

                checkedCount++;
                if (!File.Exists(fullPath))
                {
                    brokenLinks.Add($"In {Path.GetFileName(file)}: /images/{relPath} (Expected: {fullPath})");
                }
            }
        }

        Assert.True(checkedCount > 0, "Expected at least 1 image reference to be scanned across Razor and CSS.");
        Assert.Empty(brokenLinks);
    }

    [Fact]
    public void Pillar4_AppRazor_HlsAndCinemaPlayerScripts_DoNotAppearInInitialHtml()
    {
        var root = GetSolutionRoot();
        var appRazorPath = Path.Combine(root, "src", "Client", "WebXemPhim.Client", "Components", "App.razor");
        Assert.True(File.Exists(appRazorPath), $"App.razor not found at {appRazorPath}");

        var content = File.ReadAllText(appRazorPath);

        // Neither hls.min.js nor cinema-player.js must appear in App.razor
        Assert.DoesNotContain("hls.min.js", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cinema-player.js", content, StringComparison.OrdinalIgnoreCase);

        // Static fingerprinting @Assets[...] must be present
        Assert.Contains("@Assets[\"css/cinema-theme.css\"]", content);
        Assert.Contains("@Assets[\"_framework/blazor.web.js\"]", content);
    }
}
