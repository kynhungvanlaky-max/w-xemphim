using System.Net;
using System.Net.Http.Json;
using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ClientTests;

public sealed class Milestone2WatchExperienceTests
{
    private static StreamSourceDto CreateStream(string server, string quality, StreamServerType type = StreamServerType.VipFastServer, string url = "https://stream.example.com/video.mp4") => new()
    {
        ServerName = server,
        Quality = quality,
        ServerType = type,
        StreamUrl = url
    };

    private static MovieDetailDto CreateMovieWithVariedStreams() => new()
    {
        Id = 1,
        Title = "Phim Kiểm Thử Milestone 2",
        Episodes =
        [
            new EpisodeDto
            {
                Id = 10,
                EpisodeNumber = 1,
                Title = "Tập 1: Mở Đầu",
                Streams =
                [
                    CreateStream("VIP 1 (Hà Nội)", "4K Ultra HD", StreamServerType.VipFastServer, "https://stream.example.com/vip-4k.mp4"),
                    CreateStream("VIP 1 (Hà Nội)", "1080p FHD", StreamServerType.VipFastServer, "https://stream.example.com/vip-1080p.mp4"),
                    CreateStream("Hydra CDN", "1080p FHD", StreamServerType.HydraCdn, "https://stream.example.com/hydra-1080p.mp4"),
                    CreateStream("Hydra CDN", "720p HD", StreamServerType.HydraCdn, "https://stream.example.com/hydra-720p.mp4"),
                    CreateStream("Direct Backup", "480p SD", StreamServerType.BackupStream, "https://stream.example.com/backup-480p.mp4")
                ]
            }
        ]
    };

    [Fact]
    public void WatchPlaybackState_PreferredQuality_SelectsMatchingResolution()
    {
        var movie = CreateMovieWithVariedStreams();

        // 1. Preferred quality 720p selects 720p stream
        var state720 = new WatchPlaybackState
        {
            PreferredQuality = "720p HD",
            IsVipUser = true
        };
        state720.Load(movie, 1);
        Assert.NotNull(state720.Source);
        Assert.Equal("720p HD", state720.Source.Quality);

        // 2. Preferred quality 1080p selects 1080p stream
        var state1080 = new WatchPlaybackState
        {
            PreferredQuality = "1080p FHD",
            IsVipUser = true
        };
        state1080.Load(movie, 1);
        Assert.NotNull(state1080.Source);
        Assert.Equal("1080p FHD", state1080.Source.Quality);

        // 3. Preferred quality 4K selects 4K stream
        var state4k = new WatchPlaybackState
        {
            PreferredQuality = "4K khi có thể",
            IsVipUser = true
        };
        state4k.Load(movie, 1);
        Assert.NotNull(state4k.Source);
        Assert.Equal("4K Ultra HD", state4k.Source.Quality);
    }

    [Fact]
    public void WatchPlaybackState_NonVipUser_GatingRestrictsVipAnd4KStreams()
    {
        var movie = CreateMovieWithVariedStreams();
        var state = new WatchPlaybackState
        {
            IsVipUser = false
        };
        state.Load(movie, 1);

        // Non-VIP user should have been defaulted to a playable non-VIP stream (Hydra CDN 1080p or 720p)
        Assert.NotNull(state.Source);
        Assert.NotEqual(StreamServerType.VipFastServer, state.Source.ServerType);
        Assert.DoesNotContain("4K", state.Source.Quality, StringComparison.OrdinalIgnoreCase);

        // Cannot switch to VIP server
        var switchedToVip = state.SetServer("VIP 1 (Hà Nội)");
        Assert.False(switchedToVip);
        Assert.NotEqual("VIP 1 (Hà Nội)", state.Source.ServerName);

        // Cannot switch to 4K quality
        var switchedTo4K = state.SetQuality("4K Ultra HD");
        Assert.False(switchedTo4K);
    }

    [Fact]
    public void WatchPlaybackState_VipUser_PermittedAllStreamsAnd4K()
    {
        var movie = CreateMovieWithVariedStreams();
        var state = new WatchPlaybackState
        {
            IsVipUser = true
        };
        state.Load(movie, 1);

        // Can switch to VIP server
        var switchedToVip = state.SetServer("VIP 1 (Hà Nội)");
        Assert.True(switchedToVip);
        Assert.Equal("VIP 1 (Hà Nội)", state.Source!.ServerName);

        // Can switch to 4K quality
        var switchedTo4K = state.SetQuality("4K Ultra HD");
        Assert.True(switchedTo4K);
        Assert.Equal("4K Ultra HD", state.Source.Quality);
    }

    [Fact]
    public async Task WatchApiClient_GetProgressAndIncrementViewCount_ExecuteSuccessfully()
    {
        var handler = new MockDelegatingHandler((request) =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("/api/library/progress/1") == true && request.Method == HttpMethod.Get)
            {
                var progressDto = new WatchProgressDto
                {
                    MovieId = 1,
                    EpisodeNumber = 3,
                    ProgressSeconds = 600,
                    DurationSeconds = 2400
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(ApiResponse<WatchProgressDto>.Ok(progressDto))
                };
            }
            if (request.RequestUri?.AbsolutePath.Contains("/api/movies/1/view") == true && request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(ApiResponse<bool>.Ok(true))
                };
            }
            if (request.RequestUri?.AbsolutePath.Contains("/api/library/progress") == true && request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(ApiResponse<bool>.Ok(true))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("AccountApi")).Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost:5000/") });

        var accountApiMock = new Mock<IAccountApiClient>();
        accountApiMock.Setup(a => a.GetMeAsync()).ReturnsAsync(new UserDto
        {
            Id = Guid.NewGuid(),
            Email = "test@user.vn",
            MembershipTier = MembershipTier.Vip
        });

        var sessionStoreMock = new Mock<IAuthSessionStore>();
        sessionStoreMock.Setup(s => s.ReadAsync()).ReturnsAsync("test-bearer-token");

        var authState = new AuthState(accountApiMock.Object, sessionStoreMock.Object);
        await authState.InitializeAsync();

        var watchApi = new WatchApiClient(factoryMock.Object, authState);

        // 1. GetProgressAsync
        var progress = await watchApi.GetProgressAsync(1);
        Assert.NotNull(progress);
        Assert.Equal(1, progress.MovieId);
        Assert.Equal(3, progress.EpisodeNumber);
        Assert.Equal(600, progress.ProgressSeconds);

        // 2. SaveProgressAsync
        var saved = await watchApi.SaveProgressAsync(new UpdateWatchProgressRequest
        {
            MovieId = 1,
            EpisodeNumber = 3,
            ProgressSeconds = 600
        });
        Assert.True(saved);

        // 3. IncrementViewCountAsync
        var incremented = await watchApi.IncrementViewCountAsync(1);
        Assert.True(incremented);
    }

    private sealed class MockDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
