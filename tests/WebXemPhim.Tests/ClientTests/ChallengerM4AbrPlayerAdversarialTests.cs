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
    public void CinemaPlayer_HlsQualityLevels_DefaultToAuto_AndHandleDynamicSwitching()
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

        // 1. Initial parsed state defaults to Auto ABR
        Assert.Equal("Tự động (Auto ABR)", labelProp.GetValue(player));
        Assert.Equal("AUTO", badgeProp.GetValue(player));

        // 2. Switch to 480p SD
        player.OnHlsLevelSwitched(0);
        Assert.Equal("480p SD", labelProp.GetValue(player));
        Assert.Equal("SD", badgeProp.GetValue(player));

        // 3. Switch to 720p HD
        player.OnHlsLevelSwitched(1);
        Assert.Equal("720p HD", labelProp.GetValue(player));
        Assert.Equal("HD", badgeProp.GetValue(player));

        // 4. Switch to 1080p FHD
        player.OnHlsLevelSwitched(2);
        Assert.Equal("1080p FHD", labelProp.GetValue(player));
        Assert.Equal("FHD", badgeProp.GetValue(player));

        // 5. Switch to 4K Ultra HD
        player.OnHlsLevelSwitched(3);
        Assert.Equal("4K Ultra HD", labelProp.GetValue(player));
        Assert.Equal("4K", badgeProp.GetValue(player));

        // 6. Switch back to auto (-1)
        player.OnHlsLevelSwitched(-1);
        Assert.Equal("Tự động (Auto ABR)", labelProp.GetValue(player));
        Assert.Equal("AUTO", badgeProp.GetValue(player));

        // 7. Out of range level index must not throw and fall back to Auto
        player.OnHlsLevelSwitched(999);
        Assert.Equal("Tự động (Auto ABR)", labelProp.GetValue(player));
        Assert.Equal("AUTO", badgeProp.GetValue(player));

        player.OnHlsLevelSwitched(-5);
        Assert.Equal("Tự động (Auto ABR)", labelProp.GetValue(player));
        Assert.Equal("AUTO", badgeProp.GetValue(player));
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
}
