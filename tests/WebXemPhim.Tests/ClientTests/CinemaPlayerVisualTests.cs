using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WebXemPhim.Client.Components;

namespace WebXemPhim.Tests.ClientTests;

public sealed class CinemaPlayerVisualTests
{
    [Fact]
    public async Task WatchRouteLoadsAndIncludesPlayerAssets()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var js = await client.GetStringAsync("/js/cinema-player.js");
        Assert.Contains("cp-buffering-spinner", js);
        Assert.Contains("pulse-rewind", js);
        Assert.Contains("pulse-forward", js);
        Assert.Contains("setBuffering", js);

        var css = await client.GetStringAsync("/css/cinema-theme.css");
        Assert.Contains(".cp-buffering-spinner", css);
        Assert.Contains(".cp-pulse-action", css);
        Assert.Contains(".cp-seek-tag", css);
        Assert.Contains(".cp-play-btn", css);
    }

    [Fact]
    public void CinemaPlayerFileContainsBufferingAndActionIndicators()
    {
        var playerPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Player", "CinemaPlayer.razor");
        var playerContent = File.ReadAllText(playerPath);

        Assert.Contains("cp-buffering-spinner", playerContent);
        Assert.Contains("cp-spinner-ring", playerContent);
        Assert.Contains("cp-center-badge", playerContent);
        Assert.Contains("cp-pill-badge", playerContent);
        Assert.Contains("cp-action-play", playerContent);
        Assert.Contains("cp-action-pause", playerContent);
        Assert.Contains("cp-action-rewind", playerContent);
        Assert.Contains("cp-action-forward", playerContent);
        Assert.Contains("cp-rewind-btn", playerContent);
        Assert.Contains("cp-forward-btn", playerContent);
        Assert.Contains("-10s", playerContent);
        Assert.Contains("+10s", playerContent);
    }

    [Fact]
    public void CinemaIconContainsTenSecondMarkersForSeekIcons()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Icons", "CinemaIcon.razor");
        var iconContent = File.ReadAllText(iconPath);

        Assert.Contains("CinemaIconName.Rewind10", iconContent);
        Assert.Contains("CinemaIconName.Forward10", iconContent);
        Assert.Contains(">10<", iconContent);
        Assert.Contains("CinemaIconName.Grid", iconContent);
    }

    [Fact]
    public void EpisodeRailContainsAnimationClassesAndDualViewSwitcher()
    {
        var watchPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Watch.razor");
        var watchContent = File.ReadAllText(watchPath);

        Assert.Contains("wv-view-switcher", watchContent);
        Assert.Contains("wv-now-playing", watchContent);
        Assert.Contains("wv-pulse-dot", watchContent);
        Assert.Contains("wv-eq-wave", watchContent);
        Assert.Contains("wv-eq-bar", watchContent);
        Assert.Contains("wv-episode-overlay", watchContent);
        Assert.Contains("wv-grid-episodes", watchContent);
        Assert.Contains("wv-grid-item", watchContent);

        var cssPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Watch.razor.css");
        var cssContent = File.ReadAllText(cssPath);

        Assert.Contains("@keyframes wvEqBounce", cssContent);
        Assert.Contains("@keyframes wvPulseGlow", cssContent);
        Assert.Contains(".wv-eq-wave", cssContent);
        Assert.Contains(".wv-pulse-dot", cssContent);
        Assert.Contains(".wv-view-switcher", cssContent);
        Assert.Contains(".wv-grid-episodes", cssContent);
        Assert.Contains(".wv-grid-item", cssContent);
    }

    [Fact]
    public void ReviewComposerContainsCinemaRatingHubAndQuickInspirationTags()
    {
        var reviewPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Common", "ReviewPanel.razor");
        var reviewContent = File.ReadAllText(reviewPath);

        Assert.Contains("cinema-rating-hub", reviewContent);
        Assert.Contains("cinema-stars-track", reviewContent);
        Assert.Contains("cinema-stars-ribbon", reviewContent);
        Assert.Contains("cinema-star-btn", reviewContent);
        Assert.Contains("cinema-star-item", reviewContent);
        Assert.Contains("rating-hero-stat", reviewContent);
        Assert.Contains("quick-tags-wrap", reviewContent);
        Assert.Contains("quick-chip", reviewContent);
        Assert.Contains("textarea-footer", reviewContent);

        var cssPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Common", "ReviewPanel.razor.css");
        var cssContent = File.ReadAllText(cssPath);

        Assert.Contains(".cinema-rating-hub", cssContent);
        Assert.Contains(".cinema-stars-track", cssContent);
        Assert.Contains(".cinema-stars-ribbon", cssContent);
        Assert.Contains(".cinema-star-btn", cssContent);
        Assert.Contains(".cinema-star-item", cssContent);
        Assert.Contains(".score-digit", cssContent);
        Assert.Contains(".quick-chip", cssContent);
    }
}
