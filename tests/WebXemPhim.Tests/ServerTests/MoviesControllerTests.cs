using Microsoft.AspNetCore.Mvc;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class MoviesControllerTests
{
    private readonly Mock<IMovieService> _movieService = new(MockBehavior.Strict);
    private readonly MoviesController _controller;

    public MoviesControllerTests() => _controller = new MoviesController(_movieService.Object);

    [Fact]
    public async Task Filter_ForwardsExactRequestAndWrapsPagedResult()
    {
        var request = new MovieFilterRequest { Keyword = "Dune", Page = 2, PageSize = 3 };
        var paged = new PagedResult<MovieDto>
        {
            Items = [new() { Id = 1, Title = "Dune" }],
            TotalItems = 4,
            Page = 2,
            PageSize = 3
        };
        _movieService.Setup(service => service.FilterMoviesAsync(request)).ReturnsAsync(paged);

        var response = AssertOk(await _controller.Filter(request));

        Assert.True(response.Success);
        Assert.Same(paged, response.Data);
        _movieService.VerifyAll();
    }

    [Fact]
    public async Task GetFeatured_WhenFoundReturnsOkEnvelope()
    {
        var movie = new MovieDto { Id = 1, Title = "Featured", IsFeatured = true };
        _movieService.Setup(service => service.GetFeaturedMovieAsync()).ReturnsAsync(movie);

        var response = AssertOk(await _controller.GetFeatured());

        Assert.True(response.Success);
        Assert.Same(movie, response.Data);
    }

    [Fact]
    public async Task GetFeatured_WhenMissingReturnsNotFoundEnvelope()
    {
        _movieService.Setup(service => service.GetFeaturedMovieAsync()).ReturnsAsync((MovieDto?)null);

        var response = AssertNotFound(await _controller.GetFeatured());

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task GetTrending_ForwardsCountAndReturnsOk(int count)
    {
        var movies = new List<MovieDto> { new() { Id = 1 } };
        _movieService.Setup(service => service.GetTrendingMoviesAsync(count)).ReturnsAsync(movies);

        var response = AssertOk(await _controller.GetTrending(count));

        Assert.Same(movies, response.Data);
        _movieService.VerifyAll();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task GetTopSeries_ForwardsCountAndReturnsOk(int count)
    {
        var movies = new List<MovieDto> { new() { Id = 3 } };
        _movieService.Setup(service => service.GetTopSeriesAsync(count)).ReturnsAsync(movies);

        var response = AssertOk(await _controller.GetTopSeries(count));

        Assert.Same(movies, response.Data);
        _movieService.VerifyAll();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task GetLatest_ForwardsCountAndReturnsOk(int count)
    {
        var movies = new List<MovieDto> { new() { Id = 5 } };
        _movieService.Setup(service => service.GetLatestMoviesAsync(count)).ReturnsAsync(movies);

        var response = AssertOk(await _controller.GetLatest(count));

        Assert.Same(movies, response.Data);
        _movieService.VerifyAll();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GetById_WhenFoundReturnsOk(int id)
    {
        var movie = new MovieDetailDto { Id = id, Title = "Movie" };
        _movieService.Setup(service => service.GetMovieByIdAsync(id)).ReturnsAsync(movie);

        var response = AssertOk(await _controller.GetById(id));

        Assert.True(response.Success);
        Assert.Same(movie, response.Data);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task GetById_WhenMissingReturnsNotFoundWithIdInMessage(int id)
    {
        _movieService.Setup(service => service.GetMovieByIdAsync(id)).ReturnsAsync((MovieDetailDto?)null);

        var response = AssertNotFound(await _controller.GetById(id));

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Contains(id.ToString(), response.Message);
    }

    [Theory]
    [InlineData("dune-part-three")]
    [InlineData("DUNE-PART-THREE")]
    public async Task GetBySlug_WhenFoundReturnsOk(string slug)
    {
        var movie = new MovieDetailDto { Id = 1, Slug = "dune-part-three" };
        _movieService.Setup(service => service.GetMovieBySlugAsync(slug)).ReturnsAsync(movie);

        var response = AssertOk(await _controller.GetBySlug(slug));

        Assert.Same(movie, response.Data);
    }

    [Fact]
    public async Task GetBySlug_WhenMissingReturnsNotFoundWithSlugInMessage()
    {
        const string slug = "missing";
        _movieService.Setup(service => service.GetMovieBySlugAsync(slug)).ReturnsAsync((MovieDetailDto?)null);

        var response = AssertNotFound(await _controller.GetBySlug(slug));

        Assert.False(response.Success);
        Assert.Contains(slug, response.Message);
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, 0)]
    [InlineData(999, 100)]
    public async Task GetSimilar_ForwardsArgumentsAndReturnsOk(int id, int count)
    {
        var movies = new List<MovieDto>();
        _movieService.Setup(service => service.GetSimilarMoviesAsync(id, count)).ReturnsAsync(movies);

        var response = AssertOk(await _controller.GetSimilar(id, count));

        Assert.True(response.Success);
        Assert.Same(movies, response.Data);
        _movieService.VerifyAll();
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
