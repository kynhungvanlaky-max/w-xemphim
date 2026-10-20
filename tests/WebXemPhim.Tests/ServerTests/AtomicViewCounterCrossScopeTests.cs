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

public sealed class AtomicViewCounterCrossScopeTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    [Fact]
    public async Task IncrementViewCountAsync_SeparateDIScopes_ShareCooldownCacheAcrossScopes()
    {
        // 1. Setup DI container matching production registration: AddMemoryCache + AddScoped<IMovieService, DatabaseMovieService>
        var services = new ServiceCollection();
        services.AddMemoryCache();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-cross-scope-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);
        services.AddSingleton<IDbContextFactory<AppDbContext>>(contextFactory);
        services.AddScoped<IMovieService, DatabaseMovieService>();

        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = 1,
                Title = "Cross Scope Movie",
                Slug = "cross-scope-movie",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 100
            });
            await seedContext.SaveChangesAsync();
        }

        using var rootProvider = services.BuildServiceProvider();

        const string clientIp = "203.0.113.1";

        // 2. First HTTP request simulation: Scope 1 resolves IMovieService and increments
        using (var scope1 = rootProvider.CreateScope())
        {
            var service1 = scope1.ServiceProvider.GetRequiredService<IMovieService>();
            var result1 = await service1.IncrementViewCountAsync(1, clientIp);
            Assert.True(result1);
        }

        // Verify DB incremented from 100 to 101
        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(101, movie.ViewCount);
        }

        // 3. Second HTTP request simulation: Scope 2 resolves a brand-new IMovieService instance
        using (var scope2 = rootProvider.CreateScope())
        {
            var service2 = scope2.ServiceProvider.GetRequiredService<IMovieService>();
            // Must hit the shared DI singleton cache and return true WITHOUT incrementing the database
            var result2 = await service2.IncrementViewCountAsync(1, clientIp);
            Assert.True(result2);
        }

        // Verify DB was NOT incremented again across scopes (still 101)
        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(101, movie.ViewCount);
        }

        // 4. Request from a different IP in Scope 3 must increment to 102
        using (var scope3 = rootProvider.CreateScope())
        {
            var service3 = scope3.ServiceProvider.GetRequiredService<IMovieService>();
            var result3 = await service3.IncrementViewCountAsync(1, "203.0.113.2");
            Assert.True(result3);
        }

        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(102, movie.ViewCount);
        }
    }

    [Fact]
    public async Task IncrementViewCountAsync_FallbackCache_SharesCooldownAcrossUninjectedInstances()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-fallback-cache-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);

        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = 2,
                Title = "Fallback Cache Movie",
                Slug = "fallback-cache-movie",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 10
            });
            await seedContext.SaveChangesAsync();
        }

        // Two completely distinct instances instantiated without DI or explicit cache
        var serviceA = new DatabaseMovieService(contextFactory);
        var serviceB = new DatabaseMovieService(contextFactory);

        const string clientIp = "198.51.100.50";

        // Call via serviceA -> increments 10 -> 11
        var resultA = await serviceA.IncrementViewCountAsync(2, clientIp);
        Assert.True(resultA);

        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(2);
            Assert.NotNull(movie);
            Assert.Equal(11, movie.ViewCount);
        }

        // Call via serviceB with same IP -> shared static fallback cache deduplicates
        var resultB = await serviceB.IncrementViewCountAsync(2, clientIp);
        Assert.True(resultB);

        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(2);
            Assert.NotNull(movie);
            Assert.Equal(11, movie.ViewCount);
        }
    }

    [Fact]
    public async Task IncrementViewCountAsync_ParallelBurstSameIp_IncrementsOnlyOnce()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-parallel-burst-{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestContextFactory(dbOptions);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DatabaseMovieService(contextFactory, cache: cache);

        const int initialCount = 500;
        await using (var seedContext = new AppDbContext(dbOptions))
        {
            seedContext.Movies.Add(new MovieEntity
            {
                Id = 3,
                Title = "Parallel Burst Movie",
                Slug = "parallel-burst-movie",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = initialCount
            });
            await seedContext.SaveChangesAsync();
        }

        const string burstIp = "203.0.113.88";
        const int concurrentRequests = 30;

        // Launch 30 concurrent tasks simulating a millisecond burst from the same IP
        var burstTasks = Enumerable.Range(1, concurrentRequests).Select(async _ =>
        {
            await Task.Yield();
            return await service.IncrementViewCountAsync(3, burstIp);
        }).ToArray();

        var burstResults = await Task.WhenAll(burstTasks);

        // Every request must succeed (return true)
        Assert.All(burstResults, Assert.True);

        // The in-memory lock/reservation ensures the database is incremented EXACTLY ONCE (500 + 1 = 501)
        await using (var verifyContext = new AppDbContext(dbOptions))
        {
            var movie = await verifyContext.Movies.FindAsync(3);
            Assert.NotNull(movie);
            Assert.Equal(initialCount + 1, movie.ViewCount);
        }
    }

    [Fact]
    public async Task IncrementViewCount_HttpEndpoint_CrossRequestCooldownDeduplication()
    {
        int movieId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = new MovieEntity
            {
                Title = "HTTP Cross-Request Dedup Movie",
                Slug = $"http-cross-request-{Guid.NewGuid():N}",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 0
            };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
            movieId = movie.Id;
        }

        const string clientIp = "198.51.100.99";

        // 1. First HTTP POST request from clientIp
        using var client1 = factory.CreateClient();
        client1.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        var response1 = await client1.PostAsync($"/api/movies/{movieId}/view", null);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.NotNull(body1);
        Assert.True(body1.Success);
        Assert.True(body1.Data);

        // Verify database ViewCount is 1
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(movie);
            Assert.Equal(1, movie.ViewCount);
        }

        // 2. Second HTTP POST request from the same clientIp in a separate HTTP client connection
        using var client2 = factory.CreateClient();
        client2.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        var response2 = await client2.PostAsync($"/api/movies/{movieId}/view", null);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.NotNull(body2);
        Assert.True(body2.Success);

        // Verify database ViewCount remains 1 (deduplicated across HTTP requests!)
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(movie);
            Assert.Equal(1, movie.ViewCount);
        }

        // 3. Third HTTP POST request from a distinct clientIp
        using var client3 = factory.CreateClient();
        client3.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.100");
        var response3 = await client3.PostAsync($"/api/movies/{movieId}/view", null);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var body3 = await response3.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.NotNull(body3);
        Assert.True(body3.Success);

        // Verify database ViewCount increments to 2
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var movie = await db.Movies.FindAsync(movieId);
            Assert.NotNull(movie);
            Assert.Equal(2, movie.ViewCount);
        }
    }
}
