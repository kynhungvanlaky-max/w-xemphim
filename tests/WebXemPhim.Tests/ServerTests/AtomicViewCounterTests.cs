using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class AtomicViewCounterTests
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    [Fact]
    public async Task IncrementViewCountAsync_IncrementsViewCountOnFirstCall()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-count-test-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestContextFactory(options);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await using (var context = new AppDbContext(options))
        {
            context.Movies.Add(new MovieEntity
            {
                Id = 1,
                Title = "Phim Lượt Xem Test",
                Slug = "phim-luot-xem-test",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 100
            });
            await context.SaveChangesAsync();
        }

        var service = new DatabaseMovieService(factory, cache: cache);
        var result = await service.IncrementViewCountAsync(1, "192.168.1.100");

        Assert.True(result);

        await using (var context = new AppDbContext(options))
        {
            var movie = await context.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(101, movie.ViewCount);
        }
    }

    [Fact]
    public async Task IncrementViewCountAsync_DeduplicatesSameIpWithinCooldown()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-count-dedup-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestContextFactory(options);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await using (var context = new AppDbContext(options))
        {
            context.Movies.Add(new MovieEntity
            {
                Id = 1,
                Title = "Phim Lượt Xem Dedup",
                Slug = "phim-luot-xem-dedup",
                PosterUrl = "https://example.com/p.jpg",
                BackdropUrl = "https://example.com/b.jpg",
                Type = MovieType.Movie,
                ViewCount = 50
            });
            await context.SaveChangesAsync();
        }

        var service = new DatabaseMovieService(factory, cache: cache);

        // First click increments
        var first = await service.IncrementViewCountAsync(1, "10.0.0.1");
        Assert.True(first);

        // Immediate second click from same IP is throttled/deduplicated (returns true without incrementing)
        var second = await service.IncrementViewCountAsync(1, "10.0.0.1");
        Assert.True(second);

        await using (var context = new AppDbContext(options))
        {
            var movie = await context.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(51, movie.ViewCount); // Only incremented once
        }

        // Click from a different IP increments
        var differentIp = await service.IncrementViewCountAsync(1, "10.0.0.2");
        Assert.True(differentIp);

        await using (var context = new AppDbContext(options))
        {
            var movie = await context.Movies.FindAsync(1);
            Assert.NotNull(movie);
            Assert.Equal(52, movie.ViewCount);
        }
    }

    [Fact]
    public async Task IncrementViewCountAsync_NonExistentMovie_ReturnsFalse()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"view-count-nonexistent-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestContextFactory(options);
        var cache = new MemoryCache(new MemoryCacheOptions());

        var service = new DatabaseMovieService(factory, cache: cache);
        var result = await service.IncrementViewCountAsync(999999, "192.168.1.1");
        Assert.False(result);
    }

    [Fact]
    public async Task MoviesController_IncrementView_DelegatesToServiceWithClientIp()
    {
        var mockService = new Mock<IMovieService>(MockBehavior.Strict);
        mockService.Setup(s => s.IncrementViewCountAsync(10, "127.0.0.1")).ReturnsAsync(true);

        var controller = new MoviesController(mockService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1") }
                }
            }
        };

        var actionResult = await controller.IncrementView(10);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<bool>>(okResult.Value);

        Assert.True(response.Success);
        Assert.True(response.Data);
        mockService.VerifyAll();
    }
}
