using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class EpisodesControllerTests : IAsyncLifetime
{
    private readonly DbContextOptions<AppDbContext> _options;
    private EpisodesController _controller = null!;

    public EpisodesControllerTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"episodes-tests-{Guid.NewGuid():N}")
            .Options;
    }

    public async Task InitializeAsync()
    {
        await using var context = new AppDbContext(_options);
        await DatabaseSeeder.SeedCatalogAsync(context);
        var factory = new TestDbContextFactory(_options);
        _controller = new EpisodesController(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public async Task GetEpisodesByMovieId_WhenFoundReturnsAllMovieEpisodes(int movieId)
    {
        var expected = Assert.Single(DatabaseSeeder.CatalogMovies, movie => movie.Id == movieId).Episodes;

        var actionResult = await _controller.GetEpisodesByMovieId(movieId);

        var response = AssertOk(actionResult);
        Assert.True(response.Success);
        Assert.Equal(expected.Count, response.Data!.Count);
        Assert.All(response.Data, episode => Assert.Equal(movieId, episode.MovieId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8888)]
    [InlineData(9999)]
    public async Task GetEpisodesByMovieId_WhenMovieMissingReturnsNotFound(int movieId)
    {
        var actionResult = await _controller.GetEpisodesByMovieId(movieId);

        var response = AssertNotFound(actionResult);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Contains(movieId.ToString(), response.Message);
    }

    [Theory]
    [InlineData(101, 1)]
    [InlineData(301, 3)]
    [InlineData(1201, 12)]
    public async Task GetEpisodeById_WhenFoundReturnsEpisodeWithStreams(int episodeId, int movieId)
    {
        var actionResult = await _controller.GetEpisodeById(episodeId);

        var response = AssertOk(actionResult);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(episodeId, response.Data.Id);
        Assert.Equal(movieId, response.Data.MovieId);
        Assert.NotEmpty(response.Data.Streams);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(102)]
    [InlineData(999999)]
    public async Task GetEpisodeById_WhenMissingReturnsNotFound(int episodeId)
    {
        var actionResult = await _controller.GetEpisodeById(episodeId);

        var response = AssertNotFound(actionResult);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Contains(episodeId.ToString(), response.Message);
    }

    private static ApiResponse<T> AssertOk<T>(ActionResult<ApiResponse<T>> actionResult)
    {
        var objectResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(200, objectResult.StatusCode);
        return Assert.IsType<ApiResponse<T>>(objectResult.Value);
    }

    private static ApiResponse<T> AssertNotFound<T>(ActionResult<ApiResponse<T>> actionResult)
    {
        var objectResult = Assert.IsType<NotFoundObjectResult>(actionResult.Result);
        Assert.Equal(404, objectResult.StatusCode);
        return Assert.IsType<ApiResponse<T>>(objectResult.Value);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }
}
