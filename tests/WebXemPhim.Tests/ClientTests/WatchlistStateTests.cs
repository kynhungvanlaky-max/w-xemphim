using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public class WatchlistStateTests
{
    private readonly Mock<IMovieApiClient> _mockApiClient = new();

    [Fact]
    public async Task InitializeAsync_PopulatesItemsFromApi()
    {
        var initialItems = new List<WatchlistItemDto>
        {
            new() { MovieId = 1, MovieTitle = "Dune 3" },
            new() { MovieId = 2, MovieTitle = "Oppenheimer" }
        };
        _mockApiClient.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(initialItems);

        var state = new WatchlistState(_mockApiClient.Object);
        await state.InitializeAsync();

        Assert.Equal(2, state.Count);
        Assert.True(state.IsInWatchlist(1));
        Assert.True(state.IsInWatchlist(2));
        Assert.False(state.IsInWatchlist(3));
    }

    [Fact]
    public async Task ToggleWatchlistAsync_WhenNotInWatchlist_AddsMovie()
    {
        _mockApiClient.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(new List<WatchlistItemDto>());
        _mockApiClient.Setup(a => a.AddToWatchlistAsync(10)).ReturnsAsync(true);

        var state = new WatchlistState(_mockApiClient.Object);
        await state.InitializeAsync();

        bool eventFired = false;
        state.OnChange += () => eventFired = true;

        var movie = new MovieDto { Id = 10, Title = "Interstellar" };
        await state.ToggleWatchlistAsync(movie);

        Assert.True(state.IsInWatchlist(10));
        Assert.Equal(1, state.Count);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task ToggleWatchlistAsync_WhenAlreadyInWatchlist_RemovesMovie()
    {
        var initial = new List<WatchlistItemDto> { new() { MovieId = 5, MovieTitle = "Solo Leveling" } };
        _mockApiClient.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(initial);
        _mockApiClient.Setup(a => a.RemoveFromWatchlistAsync(5)).ReturnsAsync(true);

        var state = new WatchlistState(_mockApiClient.Object);
        await state.InitializeAsync();

        bool eventFired = false;
        state.OnChange += () => eventFired = true;

        var movie = new MovieDto { Id = 5, Title = "Solo Leveling" };
        await state.ToggleWatchlistAsync(movie);

        Assert.False(state.IsInWatchlist(5));
        Assert.Equal(0, state.Count);
        Assert.True(eventFired);
    }
}
