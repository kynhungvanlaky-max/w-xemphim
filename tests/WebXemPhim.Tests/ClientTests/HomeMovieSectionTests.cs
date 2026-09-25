using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebXemPhim.Client.Components.Home;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Tests.ClientTests;

public class HomeMovieSectionTests
{
    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<HomeMovieSection>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task PosterRail_UsesMovieDataAndActualRoute()
    {
        var html = await RenderAsync(new()
        {
            ["Title"] = "Phim mới",
            ["Movies"] = new List<MovieDto> { new() { Id = 42, Title = "Test movie", PosterUrl = "/poster42.png" } }
        });
        Assert.Contains("/movie/42", html);
        Assert.Contains("/poster42.png", html);
        Assert.Contains("Test movie", html);
        Assert.DoesNotContain("/movie/2", html);
    }

    [Theory]
    [InlineData("/backdrop.png", "/backdrop.png")]
    [InlineData("", "/poster.png")]
    public async Task LandscapeRail_UsesBackdropOrPosterFallback(string backdrop, string expected)
    {
        var html = await RenderAsync(new()
        {
            ["Title"] = "Latest",
            ["Landscape"] = true,
            ["Movies"] = new List<MovieDto> { new() { Id = 9, Title = "Landscape", PosterUrl = "/poster.png", BackdropUrl = backdrop } }
        });
        Assert.Contains(expected, html);
        Assert.Contains("home-landscape-card", html);
        Assert.Contains("/movie/9", html);
    }

    [Fact]
    public async Task Loading_ShowsSixSkeletonsAndNoEmptyMessage()
    {
        var html = await RenderAsync(new() { ["Title"] = "Latest", ["Loading"] = true });
        Assert.Equal(6, html.Split("home-card-skeleton").Length - 1);
        Assert.DoesNotContain("home-empty", html);
    }

    [Fact]
    public async Task Empty_OffersDiscoveryInsteadOfInventingMovies()
    {
        var html = await RenderAsync(new() { ["Title"] = "Latest" });
        Assert.Contains("home-empty", html);
        Assert.Contains("/explore", html);
        Assert.DoesNotContain("movie-card", html);
    }
}
