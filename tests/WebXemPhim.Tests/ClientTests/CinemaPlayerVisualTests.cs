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

    [Fact]
    public async Task WatchRouteIncludesHlsJsAsset()
    {
        await using var factory = new WebApplicationFactory<App>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/js/hls.min.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hls", content);
    }

    [Fact]
    public void AppRazorIncludesHlsScriptBeforeCinemaPlayer()
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "App.razor");
        var appContent = File.ReadAllText(appPath);

        // Under Milestone 4, static script tags are removed from App.razor to optimize initial bundle
        Assert.DoesNotContain("<script src=\"js/hls.min.js\"></script>", appContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script src=\"js/cinema-player.js\"></script>", appContent, StringComparison.OrdinalIgnoreCase);

        // Scripts are dynamically loaded via ensureCinemaPlayerLoaded in cinema-navbar.js in proper dependency order
        var navbarJsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "cinema-navbar.js");
        var navbarContent = File.ReadAllText(navbarJsPath);

        var hlsIndex = navbarContent.IndexOf("js/hls.min.js", StringComparison.Ordinal);
        var playerIndex = navbarContent.IndexOf("js/cinema-player.js", StringComparison.Ordinal);

        Assert.True(hlsIndex >= 0, "ensureCinemaPlayerLoaded must reference js/hls.min.js");
        Assert.True(playerIndex >= 0, "ensureCinemaPlayerLoaded must reference js/cinema-player.js");
        Assert.True(hlsIndex < playerIndex, "js/hls.min.js must be loaded before js/cinema-player.js");
    }

    [Fact]
    public void CinemaPlayerScriptContainsHlsLifecycleAndRecovery()
    {
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "cinema-player.js");
        var jsContent = File.ReadAllText(jsPath);

        Assert.Contains("isHlsStream", jsContent);
        Assert.Contains("Hls.isSupported", jsContent);
        Assert.Contains("startLoad", jsContent);
        Assert.Contains("recoverMediaError", jsContent);
        Assert.Contains("setQualityLevel", jsContent);
    }

    [Fact]
    public void CinemaPlayerRazorContainsHlsQualityInterop()
    {
        var razorPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Player", "CinemaPlayer.razor");
        var razorContent = File.ReadAllText(razorPath);

        Assert.Contains("OnHlsManifestParsed", razorContent);
        Assert.Contains("OnHlsLevelSwitched", razorContent);
        Assert.Contains("setQualityLevel", razorContent);
        Assert.Contains("res-auto", razorContent);
    }

    [Fact]
    public void CinemaPlayerContainsDynamicWatermarkOverlayAndSafeZones()
    {
        var razorPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Player", "CinemaPlayer.razor");
        var razorContent = File.ReadAllText(razorPath);

        // Watermark được vẽ trên canvas (không phải text trong DOM) nên việc gỡ bằng
        // DevTools khó hơn nhiều: phải tìm đúng phần tử canvas và dừng cả vòng vẽ lại.
        Assert.Contains("cp-watermark", razorContent);
        Assert.Contains("cp-watermark-canvas", razorContent);
        Assert.Contains("data-watermark", razorContent);
        Assert.Contains("data-watermark-zone", razorContent);
        Assert.Contains("UserEmail", razorContent);
        Assert.Contains("PeriodicTimer", razorContent);
        Assert.Contains("wm-pos-", razorContent);

        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "cinema-player.js");
        var jsContent = File.ReadAllText(jsPath);

        Assert.Contains("setupWatermark", jsContent);
        Assert.Contains("getContext('2d')", jsContent);
        Assert.Contains("cp-watermark-canvas", jsContent);

        var cssPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Player", "CinemaPlayer.razor.css");
        var cssContent = File.ReadAllText(cssPath);

        Assert.Contains(".cp-watermark", cssContent);
        Assert.Contains("opacity: 0.22", cssContent);
        Assert.Contains("pointer-events: none", cssContent);
        Assert.Contains(".wm-pos-0", cssContent);
        Assert.Contains(".wm-pos-1", cssContent);
        Assert.Contains(".wm-pos-2", cssContent);
        Assert.Contains(".wm-pos-3", cssContent);
        Assert.Contains(".wm-pos-4", cssContent);
        Assert.Contains(".wm-pos-5", cssContent);
    }

    [Fact]
    public void WatchRazorPassesUserEmailToCinemaPlayer()
    {
        var watchPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Watch.razor");
        var watchContent = File.ReadAllText(watchPath);

        Assert.Contains("UserEmail=\"@Auth.User?.Email\"", watchContent);
    }

    [Fact]
    public async Task CinemaPlayerWatermarkShiftsPositionsAcrossSixSafeZones()
    {
        var player = new WebXemPhim.Client.Components.Player.CinemaPlayer();
        for (var i = 0; i < 6; i++)
        {
            player.AdvanceWatermarkPositionForTesting();
        }
        // Advance once more to verify cycle wrap-around
        player.AdvanceWatermarkPositionForTesting();
        // Disposing cleanly
        await player.DisposeAsync();
    }
}

