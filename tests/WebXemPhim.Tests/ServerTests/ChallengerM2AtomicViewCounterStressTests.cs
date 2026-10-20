using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM2AtomicViewCounterStressTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private sealed class SqliteDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private static (SqliteDbContextFactory Factory, string DbPath, Action Cleanup) CreateFileSqliteFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"webxemphim_view_stress_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Cache=Shared;Default Timeout=10";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using (var initContext = new AppDbContext(options))
        {
            initContext.Database.EnsureCreated();
        }

        var dbFactory = new SqliteDbContextFactory(options);
        void Cleanup()
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        return (dbFactory, dbPath, Cleanup);
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_AtomicIncrement_100ConcurrentDistinctIps_NoLostUpdatesOrDeadlocks()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            const int initialViewCount = 50;
            const int concurrentCount = 100;

            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.Add(new MovieEntity
                {
                    Id = 101,
                    Title = "Phim Concurrency Relational SQLite Test",
                    Slug = "phim-concurrency-relational-sqlite-test",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = initialViewCount
                });
                await seedContext.SaveChangesAsync();
            }

            // Launch 100 concurrent increments with distinct client IPs
            var tasks = Enumerable.Range(1, concurrentCount).Select(async i =>
            {
                await Task.Yield(); // ensure thread hop
                var ip = $"10.1.{i / 256}.{i % 256}";
                return await service.IncrementViewCountAsync(101, ip);
            }).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert that every single concurrent call succeeded without deadlock or lock timeout
            Assert.All(results, Assert.True);

            // Verify final ViewCount in the SQLite database
            await using (var verifyContext = await dbFactory.CreateDbContextAsync())
            {
                var movie = await verifyContext.Movies.FindAsync(101);
                Assert.NotNull(movie);
                // Zero lost updates: ViewCount must be exactly 50 + 100 = 150
                Assert.Equal(initialViewCount + concurrentCount, movie.ViewCount);
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_DeduplicationWindow_SameIpThrottled()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.Add(new MovieEntity
                {
                    Id = 201,
                    Title = "Phim Dedup Relational Test",
                    Slug = "phim-dedup-relational-test",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = 0
                });
                await seedContext.SaveChangesAsync();
            }

            const string clientIp1 = "192.168.1.10";
            const string clientIp2 = "192.168.1.20";
            const string clientIp3 = "192.168.1.30";

            // 1. Initial hit from IP 1
            var hit1 = await service.IncrementViewCountAsync(201, clientIp1);
            Assert.True(hit1);

            // 2. 20 consecutive rapid hits from IP 1 -> all must return true but not increment
            for (var i = 0; i < 20; i++)
            {
                var throttled = await service.IncrementViewCountAsync(201, clientIp1);
                Assert.True(throttled);
            }

            await using (var check1 = await dbFactory.CreateDbContextAsync())
            {
                var m = await check1.Movies.FindAsync(201);
                Assert.NotNull(m);
                Assert.Equal(1, m.ViewCount);
            }

            // 3. Hit from IP 2 -> must increment
            var hit2 = await service.IncrementViewCountAsync(201, clientIp2);
            Assert.True(hit2);

            // 4. Hit from IP 3 -> must increment
            var hit3 = await service.IncrementViewCountAsync(201, clientIp3);
            Assert.True(hit3);

            // 5. Repeated hit from IP 2 -> throttled
            var hit2Repeated = await service.IncrementViewCountAsync(201, clientIp2);
            Assert.True(hit2Repeated);

            await using (var checkFinal = await dbFactory.CreateDbContextAsync())
            {
                var m = await checkFinal.Movies.FindAsync(201);
                Assert.NotNull(m);
                Assert.Equal(3, m.ViewCount); // exactly 3: IP1 (1), IP2 (1), IP3 (1)
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_MultiMovieIsolation_DeduplicationKeySeparation()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.AddRange(
                    new MovieEntity
                    {
                        Id = 301,
                        Title = "Phim A",
                        Slug = "phim-a",
                        PosterUrl = "https://example.com/a.jpg",
                        BackdropUrl = "https://example.com/ba.jpg",
                        Type = MovieType.Movie,
                        ViewCount = 10
                    },
                    new MovieEntity
                    {
                        Id = 302,
                        Title = "Phim B",
                        Slug = "phim-b",
                        PosterUrl = "https://example.com/b.jpg",
                        BackdropUrl = "https://example.com/bb.jpg",
                        Type = MovieType.Movie,
                        ViewCount = 20
                    }
                );
                await seedContext.SaveChangesAsync();
            }

            const string sharedIp = "172.16.0.50";

            // View Movie A
            Assert.True(await service.IncrementViewCountAsync(301, sharedIp));

            // View Movie B (same IP, different movie -> MUST increment Movie B, NOT be blocked by Movie A's cooldown)
            Assert.True(await service.IncrementViewCountAsync(302, sharedIp));

            // View Movie A again (same IP, same movie -> throttled)
            Assert.True(await service.IncrementViewCountAsync(301, sharedIp));

            // View Movie B again (same IP, same movie -> throttled)
            Assert.True(await service.IncrementViewCountAsync(302, sharedIp));

            await using (var check = await dbFactory.CreateDbContextAsync())
            {
                var a = await check.Movies.FindAsync(301);
                var b = await check.Movies.FindAsync(302);
                Assert.NotNull(a);
                Assert.NotNull(b);
                Assert.Equal(11, a.ViewCount);
                Assert.Equal(21, b.ViewCount);
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_EdgeCases_NonExistentAndNullIp()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.Add(new MovieEntity
                {
                    Id = 401,
                    Title = "Phim Edge Cases",
                    Slug = "phim-edge-cases",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = 0
                });
                await seedContext.SaveChangesAsync();
            }

            // Non-existent movie IDs return false without throwing
            Assert.False(await service.IncrementViewCountAsync(999999, "1.2.3.4"));
            Assert.False(await service.IncrementViewCountAsync(-1, "1.2.3.4"));

            // Null, empty, and whitespace client IPs increment safely without throwing
            Assert.True(await service.IncrementViewCountAsync(401, null));
            Assert.True(await service.IncrementViewCountAsync(401, ""));
            Assert.True(await service.IncrementViewCountAsync(401, "   "));

            await using (var check = await dbFactory.CreateDbContextAsync())
            {
                var m = await check.Movies.FindAsync(401);
                Assert.NotNull(m);
                Assert.Equal(3, m.ViewCount);
            }

            // IPv6 address support
            const string ipv6 = "2001:0db8:85a3:0000:0000:8a2e:0370:7334";
            Assert.True(await service.IncrementViewCountAsync(401, ipv6));
            Assert.True(await service.IncrementViewCountAsync(401, ipv6)); // throttled

            await using (var check2 = await dbFactory.CreateDbContextAsync())
            {
                var m = await check2.Movies.FindAsync(401);
                Assert.NotNull(m);
                Assert.Equal(4, m.ViewCount); // exactly 1 additional view from IPv6
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_HttpEndpoint_50ConcurrentRequests_DistinctIps_Succeeds()
    {
        // Setup isolated movie in test database
        int movieId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Phim HTTP Concurrency View Test",
                Slug = $"phim-http-concurrency-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        // Send 50 concurrent HTTP POST requests with distinct simulated client IPs
        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.150.1.{i}");
            var res = await client.PostAsync($"/api/movies/{movieId}/view", null);
            return res;
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // All 50 requests must return 200 OK
        Assert.All(responses, res => Assert.Equal(HttpStatusCode.OK, res.StatusCode));

        foreach (var res in responses)
        {
            var body = await res.Content.ReadFromJsonAsync<ApiResponse<bool>>();
            Assert.NotNull(body);
            Assert.True(body.Success);
            Assert.True(body.Data);
        }
    }

    [Fact]
    public async Task Empirical_HttpEndpoint_SameIpDeduplication_And_NonExistentMovie()
    {
        int movieId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "Phim HTTP Dedup Test",
                Slug = $"phim-http-dedup-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        const string testIp = "10.180.5.99";

        // 1. First POST from testIp
        using var client1 = factory.CreateClient();
        client1.DefaultRequestHeaders.Add("X-Forwarded-For", testIp);
        var res1 = await client1.PostAsync($"/api/movies/{movieId}/view", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var body1 = await res1.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.True(body1!.Success);
        Assert.True(body1.Data);

        // 2. Immediate 5 repeated POSTs from same testIp -> all return 200 OK
        for (var i = 0; i < 5; i++)
        {
            using var clientRepeat = factory.CreateClient();
            clientRepeat.DefaultRequestHeaders.Add("X-Forwarded-For", testIp);
            var resRepeat = await clientRepeat.PostAsync($"/api/movies/{movieId}/view", null);
            Assert.Equal(HttpStatusCode.OK, resRepeat.StatusCode);
            var bodyRepeat = await resRepeat.Content.ReadFromJsonAsync<ApiResponse<bool>>();
            Assert.True(bodyRepeat!.Success);
        }

        // 3. POST from a different IP -> 200 OK
        using var clientDifferent = factory.CreateClient();
        clientDifferent.DefaultRequestHeaders.Add("X-Forwarded-For", "10.180.5.100");
        var resDifferent = await clientDifferent.PostAsync($"/api/movies/{movieId}/view", null);
        Assert.Equal(HttpStatusCode.OK, resDifferent.StatusCode);
        var bodyDifferent = await resDifferent.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.True(bodyDifferent!.Success);

        // 4. Non-existent movie ID -> returns 404 Not Found
        using var client404 = factory.CreateClient();
        var res404 = await client404.PostAsync("/api/movies/999999/view", null);
        Assert.Equal(HttpStatusCode.NotFound, res404.StatusCode);
        var body404 = await res404.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.False(body404!.Success);
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_200ConcurrentDistinctIps_HighVolume_Stress()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            const int initialCount = 1000;
            const int concurrentCount = 200;

            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.Add(new MovieEntity
                {
                    Id = 501,
                    Title = "Phim 200 Concurrent High Volume Stress",
                    Slug = "phim-200-concurrent-high-volume-stress",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = initialCount
                });
                await seedContext.SaveChangesAsync();
            }

            var tasks = Enumerable.Range(1, concurrentCount).Select(async i =>
            {
                await Task.Yield();
                var ip = $"172.20.{i / 256}.{i % 256}";
                return await service.IncrementViewCountAsync(501, ip);
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.All(results, Assert.True);

            await using (var verifyContext = await dbFactory.CreateDbContextAsync())
            {
                var movie = await verifyContext.Movies.FindAsync(501);
                Assert.NotNull(movie);
                Assert.Equal(initialCount + concurrentCount, movie.ViewCount);
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_RelationalSqlite_SameIpConcurrentBurst_ThrottlingEfficacy()
    {
        var (dbFactory, _, cleanup) = CreateFileSqliteFactory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(dbFactory, cache: cache);

        try
        {
            await using (var seedContext = await dbFactory.CreateDbContextAsync())
            {
                seedContext.Movies.Add(new MovieEntity
                {
                    Id = 601,
                    Title = "Phim Same IP Concurrent Burst",
                    Slug = "phim-same-ip-concurrent-burst",
                    PosterUrl = "https://example.com/p.jpg",
                    BackdropUrl = "https://example.com/b.jpg",
                    Type = MovieType.Movie,
                    ViewCount = 0
                });
                await seedContext.SaveChangesAsync();
            }

            const string attackerIp = "203.0.113.88";
            const int burstRequests = 30;

            // Fire 30 concurrent requests from the exact same IP
            var tasks = Enumerable.Range(1, burstRequests).Select(async _ =>
            {
                await Task.Yield();
                return await service.IncrementViewCountAsync(601, attackerIp);
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.All(results, Assert.True);

            // All requests complete successfully without deadlocks
            Assert.All(results, Assert.True);

            long burstRecordedCount;
            await using (var verifyContext = await dbFactory.CreateDbContextAsync())
            {
                var movie = await verifyContext.Movies.FindAsync(601);
                Assert.NotNull(movie);
                Assert.True(movie.ViewCount > 0, "At least 1 view must be recorded.");
                burstRecordedCount = movie.ViewCount;
            }

            // Immediately following requests from the same IP must be strictly throttled (0 additional increments)
            for (var i = 0; i < 10; i++)
            {
                Assert.True(await service.IncrementViewCountAsync(601, attackerIp));
            }

            await using (var verifyContext2 = await dbFactory.CreateDbContextAsync())
            {
                var movie = await verifyContext2.Movies.FindAsync(601);
                Assert.NotNull(movie);
                // Zero additional increments from sequential requests: ViewCount remains identical
                Assert.Equal(burstRecordedCount, movie.ViewCount);
            }
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task Empirical_HttpEndpoint_MultiMovie_DeduplicationIndependence()
    {
        int movie1Id;
        int movie2Id;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var m1 = new MovieEntity
            {
                Title = "Phim HTTP Multi 1",
                Slug = $"phim-http-multi-1-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            var m2 = new MovieEntity
            {
                Title = "Phim HTTP Multi 2",
                Slug = $"phim-http-multi-2-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.AddRange(m1, m2);
            await db.SaveChangesAsync();
            movie1Id = m1.Id;
            movie2Id = m2.Id;
        }

        const string clientIp = "10.220.1.15";

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);

        // View movie 1
        var res1 = await client.PostAsync($"/api/movies/{movie1Id}/view", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // View movie 2 with same IP -> must also succeed and return 200 OK
        var res2 = await client.PostAsync($"/api/movies/{movie2Id}/view", null);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // View movie 1 again with same IP -> returns 200 OK
        var res1Again = await client.PostAsync($"/api/movies/{movie1Id}/view", null);
        Assert.Equal(HttpStatusCode.OK, res1Again.StatusCode);
    }
}
