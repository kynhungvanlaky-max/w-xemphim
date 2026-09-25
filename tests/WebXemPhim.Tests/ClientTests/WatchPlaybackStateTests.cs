using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Tests.ClientTests;

public sealed class WatchPlaybackStateTests
{
    private static StreamSourceDto Source(string server, string quality, string path) => new()
    { ServerName = server, Quality = quality, StreamUrl = "https://media.example.com/" + path };
    private static MovieDetailDto Movie() => new()
    {
        Episodes = [new() { Id = 3, EpisodeNumber = 5, Title = "Sau cơn mưa", Streams = [Source("A", "720p", "five.mp4")] },
            new() { Id = 1, EpisodeNumber = 1, Title = "Khởi đầu", Streams = [Source("A", "720p", "one.mp4"), Source("A", "1080p", "one-hd.mp4"), Source("B", "720p", "one-b.mp4")] },
            new() { Id = 2, EpisodeNumber = 3, Title = "Trở về", Streams = [Source("B", "720p", "three.mp4")] }]
    };
    [Theory]
    [InlineData(null, 1)] [InlineData(999, 1)] [InlineData(-1, 1)] [InlineData(3, 3)]
    public void QuerySelectsRealEpisodeOrFirst(int? query, int expected)
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), query);
        Assert.Equal(expected, state.Current!.EpisodeNumber);
        Assert.Equal([1, 3, 5], state.Episodes.Select(episode => episode.EpisodeNumber));
    }
    [Fact] public void NavigationFollowsActualOrderNotInventedEpisodeNumbers()
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), 3);
        Assert.Equal(1, state.Previous!.EpisodeNumber); Assert.Equal(5, state.Next!.EpisodeNumber);
        state.Select(1); Assert.Null(state.Previous);
        state.Select(5); Assert.Null(state.Next);
    }
    [Fact] public void QualityAndServerSelectActualStreamVariants()
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), 1);
        Assert.True(state.SetQuality("1080p")); Assert.EndsWith("one-hd.mp4", state.Source!.StreamUrl);
        Assert.True(state.SetServer("B")); Assert.Equal("720p", state.Source!.Quality);
        Assert.Equal(["720p"], state.Qualities); Assert.False(state.SetQuality("4K HDR"));
        Assert.False(state.SetServer("fake")); Assert.EndsWith("one-b.mp4", state.Source.StreamUrl);
    }
    [Fact] public void ChangingEpisodeSwitchesItsSourceAndFallsBackWhenNeeded()
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), 1); state.SetQuality("1080p"); state.Select(3);
        Assert.EndsWith("three.mp4", state.Source!.StreamUrl); state.Select(5);
        Assert.EndsWith("five.mp4", state.Source!.StreamUrl);
    }
    [Fact] public void AutoNextRespectsToggleAndLastEpisode()
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), 1);
        Assert.Equal(3, state.OnEnded()!.EpisodeNumber); state.AutoNext = false; Assert.Null(state.OnEnded());
        state.AutoNext = true; state.Select(5); Assert.Null(state.OnEnded());
    }
    [Fact] public void EmptyEpisodesAndUnavailableStreamsDoNotCreateDemoContent()
    {
        var state = new WatchPlaybackState(); state.Load(new(), null);
        Assert.Empty(state.Episodes); Assert.Null(state.Current); Assert.Null(state.Source);
        state.Load(new() { Episodes = [new() { EpisodeNumber = 1, Streams = [new() { StreamUrl = "javascript:alert(1)" }] }] }, null);
        Assert.NotNull(state.Current); Assert.Null(state.Source); Assert.Empty(state.Servers);
    }
    [Fact] public void SearchMatchesNumbersAndCaseInsensitiveTitles()
    {
        var state = new WatchPlaybackState(); state.Load(Movie(), 1);
        Assert.Single(state.Search("  TRỞ VỀ ")); Assert.Equal(5, state.Search("5").Single().EpisodeNumber);
        Assert.Empty(state.Search("missing")); Assert.Equal(3, state.Search("").Count());
    }
    [Theory] [InlineData("Tập 01: Khởi đầu", "Khởi đầu")]
    [InlineData("Khởi đầu", "Khởi đầu")] [InlineData("Tập 2: Ký ức", "Tập 2: Ký ức")]
    public void DisplayTitleDoesNotRepeatCurrentEpisodePrefix(string title, string expected)
        => Assert.Equal(expected, WatchPlaybackState.Title(new() { EpisodeNumber = 1, Title = title }));

    [Fact]
    public void MultiServerSupportsVaryingQualitiesAndDynamicFiltering()
    {
        var movie = new MovieDetailDto
        {
            Episodes =
            [
                new()
                {
                    Id = 1,
                    EpisodeNumber = 1,
                    Title = "Tập 1: Đột phá",
                    Streams =
                    [
                        // VIP 1: 4 qualities
                        Source("VIP 1 (Hà Nội - Siêu Tốc)", "4K Ultra HD", "vip1-4k.mp4"),
                        Source("VIP 1 (Hà Nội - Siêu Tốc)", "1080p 60fps", "vip1-60fps.mp4"),
                        Source("VIP 1 (Hà Nội - Siêu Tốc)", "1080p FHD", "vip1-fhd.mp4"),
                        Source("VIP 1 (Hà Nội - Siêu Tốc)", "720p HD", "vip1-hd.mp4"),

                        // VIP 2: 3 qualities
                        Source("VIP 2 (Sài Gòn - Ổn Định)", "4K HDR", "vip2-4k.mp4"),
                        Source("VIP 2 (Sài Gòn - Ổn Định)", "1080p FHD", "vip2-fhd.mp4"),
                        Source("VIP 2 (Sài Gòn - Ổn Định)", "720p HD", "vip2-hd.mp4"),

                        // Hydra CDN: 3 qualities (NO 4K)
                        Source("Hydra CDN (Quốc Tế)", "1080p FHD", "hydra-fhd.mp4"),
                        Source("Hydra CDN (Quốc Tế)", "720p HD", "hydra-hd.mp4"),
                        Source("Hydra CDN (Quốc Tế)", "480p SD", "hydra-sd.mp4"),

                        // Direct Backup: 2 qualities (only 720p and 480p)
                        Source("Direct Backup (Dự Phòng)", "720p HD", "backup-hd.mp4"),
                        Source("Direct Backup (Dự Phòng)", "480p SD", "backup-sd.mp4")
                    ]
                }
            ]
        };

        var state = new WatchPlaybackState();
        state.Load(movie, 1);

        // Verify all 4 servers are discovered
        var servers = state.Servers.ToList();
        Assert.Equal(4, servers.Count);
        Assert.Contains("VIP 1 (Hà Nội - Siêu Tốc)", servers);
        Assert.Contains("VIP 2 (Sài Gòn - Ổn Định)", servers);
        Assert.Contains("Hydra CDN (Quốc Tế)", servers);
        Assert.Contains("Direct Backup (Dự Phòng)", servers);

        // Active server starts as VIP 1
        Assert.Equal("VIP 1 (Hà Nội - Siêu Tốc)", state.Source!.ServerName);
        var vip1Qualities = state.Qualities.ToList();
        Assert.Equal(4, vip1Qualities.Count);
        Assert.Contains("4K Ultra HD", vip1Qualities);
        Assert.Contains("1080p 60fps", vip1Qualities);
        Assert.Contains("1080p FHD", vip1Qualities);
        Assert.Contains("720p HD", vip1Qualities);

        // Server metadata checks
        var vip1Info = state.GetServerInfo("VIP 1 (Hà Nội - Siêu Tốc)");
        Assert.Equal(18, vip1Info.PingMs);
        Assert.Equal("VIP 4K", vip1Info.Badge);
        Assert.Equal(4, vip1Info.QualitiesCount);
        Assert.True(vip1Info.IsVip);

        // Switch to Hydra CDN (no 4K available)
        Assert.True(state.SetServer("Hydra CDN (Quốc Tế)"));
        Assert.Equal("Hydra CDN (Quốc Tế)", state.Source.ServerName);
        var hydraQualities = state.Qualities.ToList();
        Assert.Equal(3, hydraQualities.Count);
        Assert.DoesNotContain("4K Ultra HD", hydraQualities);
        Assert.DoesNotContain("4K HDR", hydraQualities);
        Assert.Contains("1080p FHD", hydraQualities);
        Assert.Contains("720p HD", hydraQualities);
        Assert.Contains("480p SD", hydraQualities);

        // Hydra CDN metadata checks
        var hydraInfo = state.GetServerInfo("Hydra CDN (Quốc Tế)");
        Assert.Equal(38, hydraInfo.PingMs);
        Assert.Equal("CDN Fast", hydraInfo.Badge);
        Assert.Equal(3, hydraInfo.QualitiesCount);

        // Switch to Direct Backup (only 2 qualities: 720p & 480p)
        Assert.True(state.SetServer("Direct Backup (Dự Phòng)"));
        Assert.Equal("Direct Backup (Dự Phòng)", state.Source.ServerName);
        var backupQualities = state.Qualities.ToList();
        Assert.Equal(2, backupQualities.Count);
        Assert.Contains("720p HD", backupQualities);
        Assert.Contains("480p SD", backupQualities);

        // Direct Backup metadata checks
        var backupInfo = state.GetServerInfo("Direct Backup (Dự Phòng)");
        Assert.Equal(55, backupInfo.PingMs);
        Assert.Equal("Dự Phòng", backupInfo.Badge);
        Assert.Equal(2, backupInfo.QualitiesCount);

        // Changing quality on Backup works
        Assert.True(state.SetQuality("480p SD"));
        Assert.Equal("480p SD", state.Source.Quality);
        Assert.EndsWith("backup-sd.mp4", state.Source.StreamUrl);
    }

    [Theory]
    [InlineData("4K Ultra HD", "4K")]
    [InlineData("4K HDR", "4K")]
    [InlineData("1080p 60fps", "60FPS")]
    [InlineData("1080p FHD", "FHD")]
    [InlineData("720p HD", "HD")]
    [InlineData("480p SD", "SD")]
    public void ResolutionBadgesAreClassifiedAccurately(string quality, string expectedBadge)
    {
        Assert.Equal(expectedBadge, WatchPlaybackState.GetResolutionBadge(quality));
    }
}
