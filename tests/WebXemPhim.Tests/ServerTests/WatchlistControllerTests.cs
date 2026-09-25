using Microsoft.AspNetCore.Mvc;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class WatchlistControllerTests
{
    private readonly Mock<IWatchlistService> _service = new(MockBehavior.Strict);
    private readonly WatchlistController _controller;

    public WatchlistControllerTests() => _controller = new WatchlistController(_service.Object);

    [Fact]
    public async Task GetWatchlist_ReturnsItemsInSuccessEnvelope()
    {
        var items = new List<WatchlistItemDto> { new() { MovieId = 1 } };
        _service.Setup(instance => instance.GetWatchlistAsync()).ReturnsAsync(items);

        var result = await _controller.GetWatchlist();

        var response = AssertOk(result);
        Assert.True(response.Success);
        Assert.Same(items, response.Data);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    public async Task AddToWatchlist_WhenServiceSucceedsReturnsTrue(int movieId)
    {
        _service.Setup(instance => instance.AddToWatchlistAsync(movieId)).ReturnsAsync(true);

        var result = await _controller.AddToWatchlist(movieId);

        var response = AssertOk(result);
        Assert.True(response.Success);
        Assert.True(response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
        _service.VerifyAll();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task AddToWatchlist_WhenServiceRejectsReturnsBadRequest(int movieId)
    {
        _service.Setup(instance => instance.AddToWatchlistAsync(movieId)).ReturnsAsync(false);

        var result = await _controller.AddToWatchlist(movieId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<bool>>(badRequest.Value);
        Assert.False(response.Success);
        Assert.False(response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(999, false)]
    public async Task RemoveFromWatchlist_AlwaysReturnsOkWithServiceResult(int movieId, bool removed)
    {
        _service.Setup(instance => instance.RemoveFromWatchlistAsync(movieId)).ReturnsAsync(removed);

        var result = await _controller.RemoveFromWatchlist(movieId);

        var response = AssertOk(result);
        Assert.True(response.Success);
        Assert.Equal(removed, response.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, false)]
    public async Task CheckInWatchlist_ReturnsServiceResult(int movieId, bool exists)
    {
        _service.Setup(instance => instance.IsInWatchlistAsync(movieId)).ReturnsAsync(exists);

        var result = await _controller.CheckInWatchlist(movieId);

        var response = AssertOk(result);
        Assert.True(response.Success);
        Assert.Equal(exists, response.Data);
    }

    private static ApiResponse<T> AssertOk<T>(ActionResult<ApiResponse<T>> actionResult)
    {
        var objectResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(200, objectResult.StatusCode);
        return Assert.IsType<ApiResponse<T>>(objectResult.Value);
    }
}
