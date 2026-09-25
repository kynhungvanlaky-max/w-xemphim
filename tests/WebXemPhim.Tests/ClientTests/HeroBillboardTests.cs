using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WebXemPhim.Client.Components.Home;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public class HeroBillboardTests
{
    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters, WatchlistState? watchlist = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (watchlist is null)
        {
            var mockApi = new Mock<IMovieApiClient>();
            mockApi.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(new List<WatchlistItemDto>());
            watchlist = new WatchlistState(mockApi.Object);
        }
        services.AddSingleton(watchlist);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var rawHtml = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<HeroBillboard>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
        return System.Net.WebUtility.HtmlDecode(rawHtml);
    }

    [Fact]
    public async Task SingleMovie_RendersTitleBackdropAndWatchButton()
    {
        var movie = new MovieDto
        {
            Id = 99,
            Title = "Dune Messiah",
            Overview = "Epic sci-fi continuation.",
            BackdropUrl = "/images/dune-messiah.jpg",
            ReleaseYear = 2026,
            DurationMinutes = 165,
            Rating = 9.1
        };

        var html = await RenderAsync(new()
        {
            ["Movie"] = movie
        });

        Assert.Contains("hero-billboard-stage", html);
        Assert.Contains("Dune Messiah", html);
        Assert.Contains("Epic sci-fi continuation.", html);
        Assert.Contains("/watch/99", html);
        Assert.Contains("/images/dune-messiah.jpg", html);
        Assert.Contains("2026", html);
        Assert.Contains("165 phút", html);
        Assert.Contains("9.1", html);
        Assert.DoesNotContain("hero-preview-rail", html);
    }

    [Fact]
    public async Task MultipleMovies_RendersCarouselSlidesIndicatorsAndNavigationButtons()
    {
        var movies = new List<MovieDto>
        {
            new() { Id = 1, Title = "Movie One", BackdropUrl = "/m1.jpg", ReleaseYear = 2024, DurationMinutes = 120, Rating = 8.5 },
            new() { Id = 2, Title = "Movie Two", BackdropUrl = "/m2.jpg", ReleaseYear = 2025, DurationMinutes = 110, Rating = 8.8 },
            new() { Id = 3, Title = "Movie Three", BackdropUrl = "/m3.jpg", ReleaseYear = 2026, DurationMinutes = 135, Rating = 9.0 }
        };

        var html = await RenderAsync(new()
        {
            ["Movies"] = movies
        });

        Assert.Contains("hero-billboard-stage", html);
        Assert.Contains("hero-preview-rail", html);
        Assert.Contains("hero-preview-strip", html);
        Assert.Contains("hero-thumb-card", html);
        Assert.Contains("hero-slide-badge", html);
        Assert.Contains("1 / 3", html);
        Assert.Contains("hero-edge-btn", html);
        Assert.Contains("Movie One", html);
        Assert.Contains("/m1.jpg", html);
        Assert.Contains("/m2.jpg", html);
        Assert.Contains("/m3.jpg", html);
    }

    [Fact]
    public async Task MovieInWatchlist_ShowsCheckedState()
    {
        var movie = new MovieDto { Id = 77, Title = "In Watchlist Movie" };
        var mockApi = new Mock<IMovieApiClient>();
        mockApi.Setup(a => a.GetWatchlistAsync()).ReturnsAsync(new List<WatchlistItemDto>
        {
            new() { MovieId = 77, MovieTitle = "In Watchlist Movie" }
        });
        var watchlist = new WatchlistState(mockApi.Object);
        await watchlist.InitializeAsync();

        var html = await RenderAsync(new()
        {
            ["Movie"] = movie
        }, watchlist);

        Assert.Contains("Đã trong tủ phim", html);
    }
}
