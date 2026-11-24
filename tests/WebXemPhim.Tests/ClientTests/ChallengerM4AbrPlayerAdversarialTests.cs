#pragma warning disable BL0005

using System.Reflection;
using Microsoft.AspNetCore.Components;
using WebXemPhim.Client.Components.Player;
using WebXemPhim.Shared.Models;
using Xunit;

namespace WebXemPhim.Tests.ClientTests;

public sealed class ChallengerM4AbrPlayerAdversarialTests
{
    private static CinemaPlayer CreatePlayerWithSuppressedRender()
    {
        var player = new CinemaPlayer();
        // Mark pending queued render so StateHasChanged() does not attempt to invoke an unattached RenderHandle during unit tests
        var field = typeof(ComponentBase).GetField("_hasPendingQueuedRender", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(player, true);
        return player;
    }

    [Theory]
    // Standard and edge-case HLS URLs
    [InlineData("https://cdn.cinema.test/master.m3u8", true)]
    [InlineData("https://cdn.cinema.test/stream.m3u8?token=xyz123&exp=1700000000", true)]
    [InlineData("https://cdn.cinema.test/live.m3u8#track-1", true)]
    [InlineData("https://cdn.cinema.test/path/index.M3U8", true)]
    [InlineData("https://cdn.cinema.test/path/index.M3u8?auth=1", true)]
    [InlineData("/api/stream/123/master.m3u8", true)]
    [InlineData("blob:https://cinema.test/uuid.m3u8", true)]
    [InlineData("https://stream.server.test/hls/playlist.m3u8?key=val#frag", true)]
    // Non-HLS streams and trick URLs
    [InlineData("https://cdn.cinema.test/video.mp4", false)]
    [InlineData("https://cdn.cinema.test/video.mp4?format=m3u8", false)] // Query param with m3u8 should NOT fool detection
    [InlineData("https://cdn.cinema.test/video.mp4#m3u8", false)] // Hash fragment with m3u8 should NOT fool detection
    [InlineData("https://cdn.cinema.test/video.webm", false)]
    [InlineData("https://cdn.cinema.test/video.mkv", false)]
    [InlineData("https://cdn.cinema.test/video.avi", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void CinemaPlayer_IsHls_StrictAndAdversarialUrlDetection(string? url, bool expectedIsHls)
    {
        var actual = CinemaPlayer.IsHls(url);
        Assert.Equal(expectedIsHls, actual);
    }

    [Fact]
    public void CinemaPlayer_HlsQualityLevels_LockToHighestManualLevel_NeverAuto()
    {
        var player = CreatePlayerWithSuppressedRender();

        var levels = new List<HlsQualityLevelDto>
        {
            new() { Index = 0, Height = 480, Bitrate = 800000, Name = "480p SD" },
            new() { Index = 1, Height = 720, Bitrate = 2500000, Name = "720p HD" },
            new() { Index = 2, Height = 1080, Bitrate = 5000000, Name = "1080p FHD" },
            new() { Index = 3, Height = 2160, Bitrate = 15000000, Name = "4K Ultra HD" }
        };

        // Manifest parsed event from HLS.js
        player.OnHlsManifestParsed(levels);

        var labelProp = typeof(CinemaPlayer).GetProperty("DisplayQualityLabel", BindingFlags.NonPublic | BindingFlags.Instance);
        var badgeProp = typeof(CinemaPlayer).GetProperty("DisplayQualityBadge", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(labelProp);
        Assert.NotNull(badgeProp);

        // 1. Initial parsed state locks to highest explicit manual resolution (no Auto ABR)
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));

        // 2. Manual switch to 480p SD
        player.OnHlsLevelSwitched(0);
        Assert.Equal("480p SD", labelProp.GetValue(player));
        Assert.Equal("SD", badgeProp.GetValue(player));

        // 3. Manual switch to 720p HD
        player.OnHlsLevelSwitched(1);
        Assert.Equal("720p HD", labelProp.GetValue(player));
        Assert.Equal("HD", badgeProp.GetValue(player));

        // 4. Manual switch to 1080p FHD
        player.OnHlsLevelSwitched(2);
        Assert.Equal("1080p FHD", labelProp.GetValue(player));
        Assert.Equal("FHD", badgeProp.GetValue(player));

        // 5. Manual switch to 4K Ultra HD
        player.OnHlsLevelSwitched(3);
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));

        // 6. Out of range or negative level index must not throw and falls back to highest explicit level (never AUTO)
        player.OnHlsLevelSwitched(-1);
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));

        player.OnHlsLevelSwitched(999);
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));

        player.OnHlsLevelSwitched(-5);
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));
    }

    [Theory]
    [InlineData("4K Ultra HD", "4K")]
    [InlineData("1080p 60fps", "60FPS")]
    [InlineData("1080p FHD", "FHD")]
    [InlineData("720p HD", "HD")]
    [InlineData("480p SD", "SD")]
    [InlineData("AUTO", "AUTO")]
    [InlineData("", "HD")] // Default fallback
    [InlineData("360p", "HD")] // Current empirical behavior: sub-480p without '480p' token falls through to 'HD'
    public void CinemaPlayer_GetShortBadge_ResolutionMapping(string qualityInput, string expectedBadge)
    {
        var method = typeof(CinemaPlayer).GetMethod("GetShortBadge", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var actual = method.Invoke(null, [qualityInput]);
        Assert.Equal(expectedBadge, actual);
    }

    [Fact]
    public void CinemaPlayer_FatalErrorHandling_SetsMediaFailedFlag()
    {
        var player = CreatePlayerWithSuppressedRender();
        var mediaFailedField = typeof(CinemaPlayer).GetField("mediaFailed", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mediaFailedField);

        Assert.False((bool)mediaFailedField.GetValue(player)!);

        // Trigger fatal error
        player.OnHlsFatalError("Fatal network error while loading chunks");

        Assert.True((bool)mediaFailedField.GetValue(player)!);
    }

    [Fact]
    public async Task CinemaPlayer_PauseAndProgressCallbacks_DispatchCorrectly()
    {
        var player = new CinemaPlayer();
        double receivedPause = -1;
        double receivedProgress = -1;

        player.OnPause = new EventCallback<double>(null, new Action<double>(val => receivedPause = val));
        player.OnProgress = new EventCallback<double>(null, new Action<double>(val => receivedProgress = val));

        await player.OnPlayerPause(123.45);
        Assert.Equal(123.45, receivedPause);

        await player.OnPlayerProgress(678.90);
        Assert.Equal(678.90, receivedProgress);
    }

    [Fact]
    public void CinemaPlayer_OnParametersSet_FallsBackToFirstStreamWhenActiveStreamIsEmpty()
    {
        var player = new CinemaPlayer
        {
            ActiveStreamUrl = "",
            Streams =
            [
                new StreamSourceDto { ServerName = "CDN 1", StreamUrl = "https://cdn.example.com/ep1.m3u8" },
                new StreamSourceDto { ServerName = "Backup", StreamUrl = "https://cdn.example.com/ep1.mp4" }
            ]
        };

        var onParams = typeof(CinemaPlayer).GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(onParams);
        onParams.Invoke(player, null);

        Assert.Equal("https://cdn.example.com/ep1.m3u8", player.ActiveStreamUrl);
    }

    [Fact]
    public void CinemaPlayer_JsScript_EnforcesHlsDisposalAndMemoryCleanup()
    {
        var jsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "cinema-player.js");
        var js = File.ReadAllText(jsPath);

        // 1. loadSource must destroy previous _cinemaHls instance before instantiating new one
        Assert.Contains("containerEl._cinemaHls.destroy()", js);

        // 2. _cinemaDispose must destroy _cinemaHls and nullify references
        Assert.Contains("containerEl._cinemaDispose", js);
        Assert.Contains("containerEl._cinemaDotNet = null", js);

        // 3. Error recovery branches
        Assert.Contains("NETWORK_ERROR", js);
        Assert.Contains("hls.startLoad()", js);
        Assert.Contains("MEDIA_ERROR", js);
        Assert.Contains("hls.recoverMediaError()", js);
        Assert.Contains("OnHlsFatalError", js);

        // 4. Quality level switching interop
        Assert.Contains("setQualityLevel", js);
        Assert.Contains("currentLevel", js);
        Assert.Contains("switchSource", js);

        // 5. Auto quality switching must be completely removed; seamless live-cursor switching & freeze overlay present
        Assert.DoesNotContain("abrGuardTimer", js);
        Assert.DoesNotContain("runHardwareAndBufferGuard", js);
        Assert.DoesNotContain("triggerAutoDownscale", js);
        Assert.DoesNotContain("triggerAutoUpscale", js);
        Assert.DoesNotContain("startLevel: -1", js);
        Assert.Contains("lowLatencyMode: false", js);
        Assert.Contains("showSwitchOverlay", js);
        Assert.Contains("isSwitching", js);
    }

    [Fact]
    public void CinemaPlayer_AutoQualityMethodsAndMenuOptions_AreRemoved()
    {
        Assert.Null(typeof(CinemaPlayer).GetMethod("OnAutoDownscaleRequested", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Null(typeof(CinemaPlayer).GetMethod("OnAutoUpscaleRequested", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));

        var razorPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Player", "CinemaPlayer.razor");
        var razor = File.ReadAllText(razorPath);
        Assert.DoesNotContain("Tự động (Auto ABR)", razor);
        Assert.DoesNotContain("Thích ứng phần cứng", razor);
        Assert.DoesNotContain("ChooseAutoQuality", razor);
        Assert.Contains("preload=\"auto\"", razor);
    }

    [Fact]
    public void WatchRazor_PlayerKey_StaysStableAcrossQualityAndServerSwitches()
    {
        var watchPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "Components", "Pages", "Watch.razor");
        var watch = File.ReadAllText(watchPath);
        Assert.Contains("private string PlayerKey => $\"ep-{playback.Current?.Id}\";", watch);
        Assert.DoesNotContain("playback.Source?.Position}", watch);
    }

    [Fact]
    public void DatabaseSeeder_GenerateMultiServerStreams_UsesUnifiedVideoAcrossAllQualitiesAndServers()
    {
        var streams = WebXemPhim.Server.Data.DatabaseSeeder.GenerateMultiServerStreams(3301);

        Assert.Equal(12, streams.Count);
        Assert.All(streams, s =>
        {
            Assert.Contains("View_From_A_Blue_Moon_Trailer-", s.StreamUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("oceans.mp4", s.StreamUrl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sintel", s.StreamUrl, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(streams.Where(s => s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase) || s.Quality.Contains("1080p", StringComparison.OrdinalIgnoreCase)),
            s => Assert.EndsWith("-1080p.mp4", s.StreamUrl, StringComparison.Ordinal));
        Assert.All(streams.Where(s => s.Quality.Contains("720p", StringComparison.OrdinalIgnoreCase)),
            s => Assert.EndsWith("-720p.mp4", s.StreamUrl, StringComparison.Ordinal));
        Assert.All(streams.Where(s => s.Quality.Contains("480p", StringComparison.OrdinalIgnoreCase)),
            s => Assert.EndsWith("-576p.mp4", s.StreamUrl, StringComparison.Ordinal));
    }

    [Fact]
    public void LocalHlsAsset_IsGenuineLibraryAndExceeds50Kb()
    {
        var hlsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Client", "WebXemPhim.Client", "wwwroot", "js", "hls.min.js");
        Assert.True(File.Exists(hlsPath), "hls.min.js must exist locally.");

        var fileInfo = new FileInfo(hlsPath);
        Assert.True(fileInfo.Length > 50_000, $"hls.min.js must be a real bundle (>50KB), but is {fileInfo.Length} bytes.");

        var fullContent = File.ReadAllText(hlsPath);
        Assert.Contains("Hls", fullContent);
    }

    [Theory]
    [InlineData("https://w-phim-api.onrender.com/api/stream/play?episodeId=4502&pos=1&sig=abc", "/api/stream/play?episodeId=4502&pos=1&sig=abc")]
    [InlineData("https://w-phim-api.onrender.com/api/stream/manifest/tok123?sid=s1", "/api/stream/manifest/tok123?sid=s1")]
    [InlineData("http://localhost:5085/api/stream/play?episodeId=1&pos=1", "/api/stream/play?episodeId=1&pos=1")]
    [InlineData("/api/stream/play?episodeId=4502&pos=1", "/api/stream/play?episodeId=4502&pos=1")]
    [InlineData("https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4", "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4")]
    public void WatchApiClient_NormalizeStreamUrl_RoutesApiStreamsSameOriginThroughYarp(string input, string expected)
    {
        var actual = WebXemPhim.Client.Services.WatchApiClient.NormalizeStreamUrl(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CircuitClientIpProvider_AndCircuitHandler_CaptureNormalizedViewerIpIntoCircuitScope()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:203.0.113.42");
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };

        // 1. Constructor captures normalized IPv4 immediately when created inside /_blazor circuit scope
        var provider = new WebXemPhim.Client.Services.CircuitClientIpProvider(accessor);
        Assert.Equal("203.0.113.42", provider.ClientIp);

        // 2. Subsequent background events after HttpContext becomes null still retain cached circuit IP
        accessor.HttpContext = null;
        Assert.Equal("203.0.113.42", provider.ClientIp);

        // 3. Reconnect via CircuitHandler updates cached IP when viewer switches networks
        var reconnectContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        reconnectContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.88");
        accessor.HttpContext = reconnectContext;

        var handler = new WebXemPhim.Client.Services.ClientIpCircuitHandler(provider, accessor);
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Equal("198.51.100.88", provider.ClientIp);
    }
}
