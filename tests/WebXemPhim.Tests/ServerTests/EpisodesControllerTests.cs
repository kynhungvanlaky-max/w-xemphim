using Microsoft.AspNetCore.Mvc;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class EpisodesControllerTests
{
    private readonly EpisodesController _controller = new();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void GetEpisodesByMovieId_WhenFoundReturnsAllMovieEpisodes(int movieId)
    {
        var expected = Assert.Single(MockMovieDatabase.Movies, movie => movie.Id == movieId).Episodes;

        var actionResult = _controller.GetEpisodesByMovieId(movieId);

        var response = AssertOk(actionResult);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        Assert.All(response.Data!, episode => Assert.Equal(movieId, episode.MovieId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(8888)]
    [InlineData(9999)]
    public void GetEpisodesByMovieId_WhenMovieMissingReturnsNotFound(int movieId)
    {
        var actionResult = _controller.GetEpisodesByMovieId(movieId);

        var response = AssertNotFound(actionResult);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Contains(movieId.ToString(), response.Message);
    }

    [Theory]
    [InlineData(101, 1)]
    [InlineData(301, 3)]
    [InlineData(1201, 12)]
    public void GetEpisodeById_WhenFoundReturnsEpisodeWithStreams(int episodeId, int movieId)
    {
        var actionResult = _controller.GetEpisodeById(episodeId);

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
    public void GetEpisodeById_WhenMissingReturnsNotFound(int episodeId)
    {
        var actionResult = _controller.GetEpisodeById(episodeId);

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
}
