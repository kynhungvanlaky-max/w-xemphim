using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Empirical adversarial stress tests designed to challenge view count deduplication and concurrency.
/// Covers concurrent burst HTTP requests, multi-IP independence, disjoint movie isolation,
/// non-existent movie error paths, and high-concurrency cross-scope service invocations.
/// </summary>
public sealed class ViewCountEmpiricalStressTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    [Fact]
    public async Task StressTest_ConcurrentBurstSameIp_50Requests_IncrementsExactlyOnce()
    {
        int movieId;
        const int initialViews = 10;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Concurrent 50 Burst Movie",
                Slug = $"concurrent-50-burst-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = initialViews
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        const string clientIp = "198.51.100.201";
        const int concurrentCount = 50;
        using var client = factory.CreateClient();

        // Launch 50 simultaneous HTTP POST requests from the exact same client IP
        var tasks = Enumerable.Range(0, concurrentCount).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/movies/{movieId}/view");
            request.Headers.Add("X-Forwarded-For", clientIp);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>();
            Assert.NotNull(body);
            Assert.True(body.Success);
            Assert.True(body.Data);
            return true;
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(concurrentCount, results.Length);
        Assert.All(results, Assert.True);

        // Verify the database view count was incremented EXACTLY ONCE (10 -> 11)
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(movie);
            Assert.Equal(initialViews + 1, movie.ViewCount);
        }
    }



    [Fact]
    public async Task StressTest_ConcurrentMultiIpBurst_DirectServiceLayer_IncrementsPerUniqueIp()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-multi-ip-service-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);
        services.AddSingleton<IDbContextFactory<AppDbContext>>(contextFactory);
        services.AddScoped<IMovieService, DatabaseMovieService>();

        const int initialViews = 50;
        const int testMovieId = 77;
        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = testMovieId,
                Title = "Concurrent Multi-IP Direct Movie",
                Slug = "concurrent-multi-ip-direct",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = initialViews
            });
            await seedContext.SaveChangesAsync();
        }

        using var rootProvider = services.BuildServiceProvider();
        const int uniqueIpsCount = 10;
        const int requestsPerIp = 5;
        var uniqueIps = Enumerable.Range(1, uniqueIpsCount).Select(i => $"198.51.100.{10 + i}").ToArray();

        var allTasks = uniqueIps.SelectMany(ip =>
            Enumerable.Range(0, requestsPerIp).Select(async _ =>
            {
                await Task.Yield();
                using var scope = rootProvider.CreateScope();
                var movieService = scope.ServiceProvider.GetRequiredService<IMovieService>();
                return await movieService.IncrementViewCountAsync(testMovieId, ip);
            })
        ).ToArray();

        var results = await Task.WhenAll(allTasks);
        Assert.Equal(uniqueIpsCount * requestsPerIp, results.Length);
        Assert.All(results, Assert.True);

        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(testMovieId);
            Assert.NotNull(movie);
            Assert.Equal(initialViews + uniqueIpsCount, movie.ViewCount);
        }
    }

    [Fact]
    public async Task StressTest_ConcurrentMultiIpBurst_HttpEndpoint_WithDistinctClients()
    {
        int movieId;
        const int initialViews = 50;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Concurrent Multi-IP Burst HTTP Movie",
                Slug = $"concurrent-multi-ip-http-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = initialViews
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        const int uniqueIpsCount = 5;
        const int requestsPerIp = 3;
        var uniqueIps = Enumerable.Range(1, uniqueIpsCount).Select(i => $"198.51.100.{20 + i}").ToArray();

        var allTasks = uniqueIps.SelectMany(ip =>
            Enumerable.Range(0, requestsPerIp).Select(async _ =>
            {
                using var client = factory.CreateClient();
                client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
                var response = await client.PostAsync($"/api/movies/{movieId}/view", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>();
                Assert.NotNull(body);
                Assert.True(body.Success);
                return true;
            })
        ).ToArray();

        var results = await Task.WhenAll(allTasks);
        Assert.Equal(uniqueIpsCount * requestsPerIp, results.Length);
        Assert.All(results, Assert.True);

        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(movie);
            Assert.Equal(initialViews + uniqueIpsCount, movie.ViewCount);
        }
    }

    [Fact]
    public async Task SequentialMultiIp_HttpEndpoint_DeduplicatesSameIpAndIncrementsDistinctIps()
    {
        int movieId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Sequential Multi-IP Movie",
                Slug = $"sequential-multi-ip-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        const string ipA = "198.51.100.1";
        const string ipB = "198.51.100.2";
        const string ipC = "198.51.100.3";

        async Task SendViewAsync(string ip)
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
            var res = await client.PostAsync($"/api/movies/{movieId}/view", null);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        async Task<long> GetViewsAsync()
        {
            using var scope = factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var m = await db.Movies.FindAsync(movieId);
            return m!.ViewCount;
        }

        // IP A -> 1
        await SendViewAsync(ipA);
        Assert.Equal(1, await GetViewsAsync());

        // IP A again -> still 1
        await SendViewAsync(ipA);
        Assert.Equal(1, await GetViewsAsync());

        // IP B -> 2
        await SendViewAsync(ipB);
        Assert.Equal(2, await GetViewsAsync());

        // IP B again -> still 2
        await SendViewAsync(ipB);
        Assert.Equal(2, await GetViewsAsync());

        // IP C -> 3
        await SendViewAsync(ipC);
        Assert.Equal(3, await GetViewsAsync());

        // IP A again -> still 3
        await SendViewAsync(ipA);
        Assert.Equal(3, await GetViewsAsync());
    }

    [Fact]
    public async Task StressTest_ConcurrentDisjointMovies_SameIp_IncrementsEachMovieOnce()
    {
        var movieIds = new List<int>();
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            for (var i = 1; i <= 3; i++)
            {
                var movie = new MovieEntity
                {
                    Title = $"Disjoint Movie {i}",
                    Slug = $"disjoint-movie-{i}-{Guid.NewGuid():N}",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = 100 * i
                };
                db.Movies.Add(movie);
                await db.SaveChangesAsync();
                movieIds.Add(movie.Id);
            }
        }

        const string clientIp = "198.51.100.75";
        using var client = factory.CreateClient();

        // For each of the 3 movies, launch 15 concurrent requests from the same IP (45 total)
        var tasks = movieIds.SelectMany(id =>
            Enumerable.Range(0, 15).Select(async _ =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/movies/{id}/view");
                request.Headers.Add("X-Forwarded-For", clientIp);
                var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>();
                Assert.NotNull(body);
                Assert.True(body.Success);
                return (MovieId: id, Success: body.Success);
            })
        ).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(45, results.Length);

        // Verify each movie's view count incremented by exactly 1
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            for (var i = 0; i < movieIds.Count; i++)
            {
                var id = movieIds[i];
                var expected = (100 * (i + 1)) + 1;
                var movie = await db.Movies.FindAsync(id);
                Assert.NotNull(movie);
                Assert.Equal(expected, movie.ViewCount);
            }
        }
    }

    [Fact]
    public async Task StressTest_ConcurrentBurstNonExistentMovie_Returns404_NoDeadlockOrLockLeak()
    {
        const int nonExistentMovieId = 987654;
        const string clientIp = "198.51.100.80";
        using var client = factory.CreateClient();

        // 30 concurrent requests to a non-existent movie
        var tasks = Enumerable.Range(0, 30).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/movies/{nonExistentMovieId}/view");
            request.Headers.Add("X-Forwarded-For", clientIp);
            var response = await client.SendAsync(request);
            return response.StatusCode;
        }).ToArray();

        var statuses = await Task.WhenAll(tasks);
        var notFoundCount = statuses.Count(s => s == HttpStatusCode.NotFound);
        var okCount = statuses.Count(s => s == HttpStatusCode.OK);
        Assert.True(okCount == 0, $"Bug found! Non-existent movie returned OK {okCount} times and NotFound {notFoundCount} times due to check-then-act race window in cache reservation.");
        Assert.Equal(30, notFoundCount);
        Assert.Equal(30, statuses.Length);

        // Followed immediately by a valid movie request from the SAME IP
        int validMovieId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Valid Movie After 404",
                Slug = $"valid-after-404-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            validMovieId = movie.Id;
        }

        using var validRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/movies/{validMovieId}/view");
        validRequest.Headers.Add("X-Forwarded-For", clientIp);
        var validResponse = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(validMovieId);
            Assert.NotNull(movie);
            Assert.Equal(1, movie.ViewCount);
        }
    }

    [Fact]
    public async Task StressTest_HighConcurrency_100ScopedInstances_SameIp_DirectServiceLayer()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-highconcurrency-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);
        services.AddSingleton<IDbContextFactory<AppDbContext>>(contextFactory);
        services.AddScoped<IMovieService, DatabaseMovieService>();

        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = 99,
                Title = "100 Scopes Stress Movie",
                Slug = "100-scopes-stress-movie",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            });
            await seedContext.SaveChangesAsync();
        }

        using var rootProvider = services.BuildServiceProvider();
        const string clientIp = "192.0.2.199";
        const int totalScopes = 100;

        // 100 concurrent tasks, each resolving IMovieService from its own new DI scope
        var tasks = Enumerable.Range(0, totalScopes).Select(async _ =>
        {
            await Task.Yield();
            using var scope = rootProvider.CreateScope();
            var movieService = scope.ServiceProvider.GetRequiredService<IMovieService>();
            return await movieService.IncrementViewCountAsync(99, clientIp);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(totalScopes, results.Length);
        Assert.All(results, Assert.True);

        // Database must increment from 0 to 1 exactly once
        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(99);
            Assert.NotNull(movie);
            Assert.Equal(1, movie.ViewCount);
        }
    }

    [Fact]
    public async Task StressTest_NullOrEmptyClientIp_IncrementsEachCallDeterministically()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-null-ip-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(contextFactory, cache: cache);

        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = 101,
                Title = "Null IP Movie",
                Slug = "null-ip-movie",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            });
            await seedContext.SaveChangesAsync();
        }

        // Sequential calls with null, empty, whitespace
        var res1 = await service.IncrementViewCountAsync(101, null);
        var res2 = await service.IncrementViewCountAsync(101, "");
        var res3 = await service.IncrementViewCountAsync(101, "   ");

        Assert.True(res1);
        Assert.True(res2);
        Assert.True(res3);

        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(101);
            Assert.NotNull(movie);
            Assert.Equal(3, movie.ViewCount);
        }
    }
}
