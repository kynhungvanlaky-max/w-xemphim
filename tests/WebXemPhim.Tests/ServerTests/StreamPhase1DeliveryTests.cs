using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

using WebXemPhim.Shared.Models;
namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// P1-02 → P1-06: token segment AES-GCM, rewrite manifest HLS, circuit breaker theo
/// origin, telemetry QoE và tổng hợp dashboard quản trị.
/// </summary>
public sealed class StreamPhase1DeliveryTests
{
    private static IConfiguration SegmentConfig(
        string key = "phase1-segment-test-key",
        int ttlSeconds = 600,
        int clockSkewSeconds = 30) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:StreamSegmentKey"] = key,
                ["Security:StreamSegmentTtlSeconds"] = ttlSeconds.ToString(),
                ["Security:StreamTokenClockSkewSeconds"] = clockSkewSeconds.ToString()
            })
            .Build();

    private static DbContextOptions<AppDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}")
            .Options;

    [Fact]
    public void SegmentToken_RoundTrip_PreservesPayloadAndKind()
    {
        var config = SegmentConfig();
        var protector = new StreamSegmentTokenProtector(config);

        var token = protector.Create(
            101,
            2,
            "session-abc",
            "ip-hash",
            "https://cdn.example.com/hls/seg-1.m4s?token=origin",
            StreamSegmentKind.Segment);

        var result = protector.Read(token, StreamSegmentKind.Segment);

        Assert.True(result.IsValid);
        var payload = Assert.IsType<StreamSegmentTokenPayload>(result.Payload);
        Assert.Equal(101, payload.EpisodeId);
        Assert.Equal(2, payload.Position);
        Assert.Equal("session-abc", payload.SessionId);
        Assert.Equal("ip-hash", payload.IpHash);
        Assert.Equal("https://cdn.example.com/hls/seg-1.m4s?token=origin", payload.UpstreamUrl);
        Assert.Equal(StreamSegmentKind.Segment, payload.Kind);
    }

    [Fact]
    public void SegmentToken_ManifestAndSegment_AreNotInterchangeable()
    {
        var protector = new StreamSegmentTokenProtector(SegmentConfig());
        var token = protector.Create(
            101,
            1,
            null,
            "ip-hash",
            "https://cdn.example.com/hls/index.m3u8",
            StreamSegmentKind.Manifest);

        var result = protector.Read(token, StreamSegmentKind.Segment);

        Assert.False(result.IsValid);
        Assert.Equal(StreamSegmentTokenStatus.WrongKind, result.Status);
        Assert.NotNull(result.Payload);
    }

    [Fact]
    public void SegmentToken_TamperedCiphertext_IsRejected()
    {
        var protector = new StreamSegmentTokenProtector(SegmentConfig());
        var token = protector.Create(
            101,
            1,
            null,
            "ip-hash",
            "https://cdn.example.com/hls/seg-1.m4s",
            StreamSegmentKind.Segment);

        var replacement = token[^5] == 'A' ? 'B' : 'A';
        var tampered = token[..^5] + replacement + token[^4..];

        var result = protector.Read(tampered, StreamSegmentKind.Segment);

        Assert.False(result.IsValid);
        Assert.Equal(StreamSegmentTokenStatus.Malformed, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void SegmentToken_ExpiredBeyondClockSkew_IsRejected()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
        var config = SegmentConfig(ttlSeconds: 60, clockSkewSeconds: 0);
        var protector = new StreamSegmentTokenProtector(config, timeProvider);

        var token = protector.Create(
            101,
            1,
            null,
            "ip-hash",
            "https://cdn.example.com/hls/seg-1.m4s",
            StreamSegmentKind.Segment,
            TimeSpan.FromSeconds(1));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = protector.Read(token, StreamSegmentKind.Segment);

        Assert.False(result.IsValid);
        Assert.Equal(StreamSegmentTokenStatus.Expired, result.Status);
        Assert.NotNull(result.Payload);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://cdn.example.com/seg.m4s")]
    [InlineData("javascript:alert(1)")]
    public void SegmentToken_RejectsNonHttpUpstream(string upstream)
    {
        var protector = new StreamSegmentTokenProtector(SegmentConfig());

        Assert.Throws<ArgumentException>(() => protector.Create(
            101,
            1,
            null,
            "ip-hash",
            upstream,
            StreamSegmentKind.Segment));
    }

    [Fact]
    public void ManifestRewriter_MasterPlaylist_RewritesVariantsAudioAndIframe()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="Vietnamese",URI="audio/vi.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,AUDIO="aud"
            1080p/index.m3u8
            #EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=800000,URI="iframe/index.m3u8"
            """;

        var rewritten = HlsManifestRewriter.Rewrite(
            manifest,
            new Uri("https://cdn.example.com/hls/master.m3u8"),
            (uri, isManifest) => $"{(isManifest ? "M" : "S")}:{uri}");

        Assert.Contains("URI=\"M:https://cdn.example.com/hls/audio/vi.m3u8\"", rewritten);
        Assert.Contains("M:https://cdn.example.com/hls/1080p/index.m3u8", rewritten);
        Assert.Contains("URI=\"M:https://cdn.example.com/hls/iframe/index.m3u8\"", rewritten);
        Assert.DoesNotContain("cdn.example.com/hls/master.m3u8", rewritten);
    }

    [Fact]
    public void ManifestRewriter_MediaPlaylist_RewritesSegmentsKeysMapsAndParts()
    {
        const string manifest = """
            #EXTM3U
            #EXT-X-VERSION:9
            #EXT-X-KEY:METHOD=AES-128,URI="keys/key.bin"
            #EXT-X-MAP:URI="init.mp4"
            #EXT-X-PART:DURATION=0.5,URI="part-1.m4s"
            #EXTINF:4.0,
            seg-1.m4s
            #EXTINF:4.0,
            https://other.cdn.example.com/seg-2.m4s?token=abc
            """;

        var rewritten = HlsManifestRewriter.Rewrite(
            manifest,
            new Uri("https://cdn.example.com/hls/1080p/index.m3u8"),
            (uri, isManifest) => $"{(isManifest ? "M" : "S")}:{uri}");

        Assert.Contains("URI=\"S:https://cdn.example.com/hls/1080p/keys/key.bin\"", rewritten);
        Assert.Contains("URI=\"S:https://cdn.example.com/hls/1080p/init.mp4\"", rewritten);
        Assert.Contains("URI=\"S:https://cdn.example.com/hls/1080p/part-1.m4s\"", rewritten);
        Assert.Contains("S:https://cdn.example.com/hls/1080p/seg-1.m4s", rewritten);
        Assert.Contains("S:https://other.cdn.example.com/seg-2.m4s?token=abc", rewritten);
    }

    [Fact]
    public void ManifestRewriter_LeavesDataUrisAndPreservesCrlf()
    {
        var manifest = "#EXTM3U\r\n#EXT-X-KEY:METHOD=AES-128,URI=\"data:text/plain;base64,AAAA\"\r\n#EXTINF:4,\r\nseg.m4s\r\n";

        var rewritten = HlsManifestRewriter.Rewrite(
            manifest,
            new Uri("https://cdn.example.com/hls/index.m3u8"),
            (uri, _) => $"X:{uri}");

        Assert.Contains("URI=\"data:text/plain;base64,AAAA\"", rewritten);
        Assert.Contains("X:https://cdn.example.com/hls/seg.m4s", rewritten);
        Assert.Contains("\r\n", rewritten);
        Assert.DoesNotContain("\n", rewritten.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://cdn.example.com/master.m3u8", true)]
    [InlineData("https://cdn.example.com/live.M3U8?token=1", true)]
    [InlineData("https://cdn.example.com/video.mp4", false)]
    [InlineData("/api/stream/play?episodeId=1", false)]
    [InlineData("", false)]
    public void LooksLikeHlsUrl_DetectsManifestPaths(string url, bool expected)
    {
        Assert.Equal(expected, HlsManifestRewriter.LooksLikeHlsUrl(url));
    }

    [Fact]
    public void CircuitBreaker_OpensAfterThresholdAndResetsAfterCooldown()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
        var breaker = new StreamOriginCircuitBreaker(timeProvider, failureThreshold: 3, openDuration: TimeSpan.FromSeconds(30));
        const string origin = "https://cdn.example.com:443";

        breaker.RecordFailure(origin);
        breaker.RecordFailure(origin);
        Assert.False(breaker.IsOpen(origin));

        breaker.RecordFailure(origin);
        Assert.True(breaker.IsOpen(origin));

        timeProvider.Advance(TimeSpan.FromSeconds(29));
        Assert.True(breaker.IsOpen(origin));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.False(breaker.IsOpen(origin));
    }

    [Fact]
    public void CircuitBreaker_SuccessResetsFailureCount()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
        var breaker = new StreamOriginCircuitBreaker(timeProvider, failureThreshold: 3, openDuration: TimeSpan.FromSeconds(30));
        const string origin = "https://cdn.example.com:443";

        breaker.RecordFailure(origin);
        breaker.RecordFailure(origin);
        breaker.RecordSuccess(origin);
        breaker.RecordFailure(origin);
        breaker.RecordFailure(origin);

        Assert.False(breaker.IsOpen(origin));
    }

    [Fact]
    public async Task TelemetryController_WritesValidBeaconWithAuthenticatedUser()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-10-26T10:00:00Z"));
        var controller = new PlaybackTelemetryController(
            factory,
            timeProvider,
            NullLogger<PlaybackTelemetryController>.Instance);

        var userId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Test"))
            }
        };

        var result = await controller.Record(new PlaybackTelemetryRequest
        {
            EventType = "startup",
            MovieId = 7,
            EpisodeId = 101,
            SessionId = "sess-1",
            Position = 2,
            ServerName = "VIP 1",
            Quality = "4K",
            Value = 1234
        });

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var entity = await db.PlaybackTelemetry.SingleAsync();
        Assert.Equal(userId, entity.UserId);
        Assert.Equal("startup", entity.EventType);
        Assert.Equal(1234, entity.Value);
        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, entity.CreatedAtUtc);
    }

    [Fact]
    public async Task TelemetryController_RejectsUnknownEventTypeAndOutOfRangeValue()
    {
        var factory = new TestDbContextFactory(InMemoryOptions());
        var controller = new PlaybackTelemetryController(
            factory,
            TimeProvider.System,
            NullLogger<PlaybackTelemetryController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var unknown = await controller.Record(new PlaybackTelemetryRequest
        {
            EventType = "hack",
            MovieId = 1,
            EpisodeId = 1
        });
        Assert.IsType<BadRequestObjectResult>(unknown.Result);

        var tooLarge = await controller.Record(new PlaybackTelemetryRequest
        {
            EventType = "rebuffer",
            MovieId = 1,
            EpisodeId = 1,
            Value = long.MaxValue
        });
        Assert.IsType<BadRequestObjectResult>(tooLarge.Result);
    }

    [Fact]
    public async Task AdminTelemetrySummary_AggregatesSessionsStartupRebufferAndErrors()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
        var timeProvider = new MutableTimeProvider(now);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.PlaybackTelemetry.AddRange(
                new PlaybackTelemetryEntity
                {
                    UserId = Guid.NewGuid(), MovieId = 1, EpisodeId = 10, SessionId = "s1",
                    ServerName = "VIP 1", Quality = "4K", EventType = "startup", Value = 1000,
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-10)
                },
                new PlaybackTelemetryEntity
                {
                    UserId = Guid.NewGuid(), MovieId = 1, EpisodeId = 10, SessionId = "s1",
                    ServerName = "VIP 1", Quality = "4K", EventType = "startup", Value = 2000,
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-9)
                },
                new PlaybackTelemetryEntity
                {
                    UserId = Guid.NewGuid(), MovieId = 1, EpisodeId = 10, SessionId = "s1",
                    ServerName = "VIP 1", Quality = "4K", EventType = "rebuffer", Value = 500,
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-8)
                },
                new PlaybackTelemetryEntity
                {
                    UserId = Guid.NewGuid(), MovieId = 1, EpisodeId = 11, SessionId = "s2",
                    ServerName = "Hydra", Quality = "1080p", EventType = "startup", Value = 3000,
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-7)
                },
                new PlaybackTelemetryEntity
                {
                    UserId = Guid.NewGuid(), MovieId = 1, EpisodeId = 11, SessionId = "s2",
                    ServerName = "Hydra", Quality = "1080p", EventType = "error", Value = 0,
                    ErrorCode = "HLS_NETWORK_FATAL",
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-6)
                });
            await db.SaveChangesAsync();
        }

        var controller = new AdminTelemetryController(factory, timeProvider);
        var result = await controller.GetSummary(24);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<WebXemPhim.Shared.Models.AdminTelemetrySummaryDto>>(ok.Value);
        var summary = Assert.IsType<WebXemPhim.Shared.Models.AdminTelemetrySummaryDto>(envelope.Data);

        Assert.Equal(5, summary.TotalEvents);
        Assert.Equal(2, summary.Sessions);
        Assert.Equal(3, summary.StartupSamples);
        Assert.Equal(2000, summary.AvgStartupMs);
        Assert.Equal(3000, summary.P95StartupMs);
        Assert.Equal(1, summary.RebufferEvents);
        Assert.Equal(1, summary.ErrorEvents);
        Assert.Equal(0.5, summary.RebufferRatePerSession);
        Assert.Equal(0.5, summary.ErrorRatePerSession);
        Assert.NotEmpty(summary.Timeline);
        Assert.Contains(summary.EventTypes, item => item.EventType == "startup" && item.Count == 3);
        Assert.Contains(summary.TopSources, item => item.ServerName == "Hydra" && item.Errors == 1);
        Assert.Single(summary.RecentErrors);
    }

    [Fact]
    public async Task AdminTelemetryEvents_FiltersByTypeAndPaging()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
        var timeProvider = new MutableTimeProvider(now);

        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 6; i++)
            {
                db.PlaybackTelemetry.Add(new PlaybackTelemetryEntity
                {
                    MovieId = 1,
                    EpisodeId = 10,
                    SessionId = "s1",
                    EventType = i % 2 == 0 ? "startup" : "error",
                    Value = i,
                    ErrorCode = i % 2 == 0 ? null : "ERR",
                    CreatedAtUtc = now.UtcDateTime.AddMinutes(-i)
                });
            }

            await db.SaveChangesAsync();
        }

        var controller = new AdminTelemetryController(factory, timeProvider);
        var result = await controller.GetEvents(eventType: "error", hours: 24, page: 1, pageSize: 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PagedResult<WebXemPhim.Shared.Models.AdminTelemetryEventDto>>>(ok.Value);
        var paged = Assert.IsType<PagedResult<WebXemPhim.Shared.Models.AdminTelemetryEventDto>>(envelope.Data);

        Assert.Equal(3, paged.TotalItems);
        Assert.Equal(2, paged.Items.Count);
        Assert.All(paged.Items, item => Assert.Equal("error", item.EventType));
    }

    [Fact]
    public async Task EdgeAuthorize_AcceptsValidSegmentTokenAndRejectsTamperedOne()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var signer = new StreamUrlSigner();
        var config = SegmentConfig();
        var protector = new StreamSegmentTokenProtector(config);
        const string clientIp = "127.0.0.1";

        var controller = new StreamController(
            factory,
            signer,
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            new Mock<IStreamSessionService>().Object,
            cache: null,
            segmentTokens: protector,
            circuitBreaker: new StreamOriginCircuitBreaker(),
            configuration: config);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var token = protector.Create(
            101,
            1,
            null,
            signer.ComputeIpHash(clientIp),
            "https://cdn.example.com/hls/seg-1.m4s",
            StreamSegmentKind.Segment);

        controller.Request.Headers["X-Original-URI"] = $"/api/stream/segment/{token}";
        var accepted = await controller.EdgeAuthorize(CancellationToken.None);
        Assert.IsType<NoContentResult>(accepted);

        var tampered = token[..^5] + (token[^5] == 'A' ? 'B' : 'A') + token[^4..];
        controller.Request.Headers["X-Original-URI"] = $"/api/stream/segment/{tampered}";
        var rejected = await controller.EdgeAuthorize(CancellationToken.None);
        var rejectedResult = Assert.IsType<ObjectResult>(rejected);
        Assert.Equal(StatusCodes.Status403Forbidden, rejectedResult.StatusCode);
    }

    [Fact]
    public async Task EdgeAuthorize_RejectsTokenIssuedForAnotherIp()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var signer = new StreamUrlSigner();
        var config = SegmentConfig();
        var protector = new StreamSegmentTokenProtector(config);

        var controller = new StreamController(
            factory,
            signer,
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            new Mock<IStreamSessionService>().Object,
            cache: null,
            segmentTokens: protector,
            circuitBreaker: new StreamOriginCircuitBreaker(),
            configuration: config);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var token = protector.Create(
            101,
            1,
            null,
            signer.ComputeIpHash("203.0.113.50"),
            "https://cdn.example.com/hls/seg-1.m4s",
            StreamSegmentKind.Segment);

        controller.Request.Headers["X-Original-URI"] = $"/api/stream/segment/{token}";
        var result = await controller.EdgeAuthorize(CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task EdgeAuthorize_HandlesOriginalUriWithSidQuery()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
        var factory = new TestDbContextFactory(options);
        var signer = new StreamUrlSigner();
        var config = SegmentConfig();
        var protector = new StreamSegmentTokenProtector(config);
        const string clientIp = "127.0.0.1";

        var sessions = new Mock<IStreamSessionService>();
        sessions
            .Setup(service => service.GetActiveLeaseAsync("session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebXemPhim.Shared.Models.ActiveStreamDto
            {
                SessionId = "session-1",
                EpisodeId = 101
            });

        var controller = new StreamController(
            factory,
            signer,
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            sessions.Object,
            cache: null,
            segmentTokens: protector,
            circuitBreaker: new StreamOriginCircuitBreaker(),
            configuration: config);

        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var token = protector.Create(
            101,
            1,
            "session-1",
            signer.ComputeIpHash(clientIp),
            "https://cdn.example.com/hls/seg-1.m4s",
            StreamSegmentKind.Segment);

        controller.Request.Headers["X-Original-URI"] = $"/api/stream/segment/{token}?sid=session-1";
        var result = await controller.EdgeAuthorize(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}

/// <summary>
/// Kiểm thử tích hợp beacon QoE qua HTTP: bắt buộc đăng nhập, ghi đúng DB và
/// trả 400 với loại sự kiện không hợp lệ.
/// </summary>
public sealed class StreamPhase1TelemetryIntegrationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task TelemetryEndpoint_RequiresAuthenticationAndPersistsBeacon()
    {
        using var anonymous = factory.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync("/api/stream/telemetry", new PlaybackTelemetryRequest
        {
            EventType = "startup",
            MovieId = 1,
            EpisodeId = 1,
            Value = 500
        });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Telemetry Tester",
            Email = $"telemetry-{Guid.NewGuid():N}@test.vn",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        register.EnsureSuccessStatusCode();
        var auth = (await register.Content.ReadFromJsonAsync<ApiResponse<WebXemPhim.Shared.Models.AuthDto>>())!.Data!;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/stream/telemetry", new PlaybackTelemetryRequest
        {
            EventType = "rebuffer",
            MovieId = 1,
            EpisodeId = 1,
            SessionId = "integration-session",
            Position = 1,
            ServerName = "VIP 1",
            Quality = "4K",
            Value = 850
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        Assert.True(payload!.Success);
        Assert.True(payload.Data);

        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.PlaybackTelemetry.SingleAsync(item => item.SessionId == "integration-session");
        Assert.Equal("rebuffer", entity.EventType);
        Assert.Equal(850, entity.Value);
        Assert.Equal("VIP 1", entity.ServerName);

        var invalid = await client.PostAsJsonAsync("/api/stream/telemetry", new PlaybackTelemetryRequest
        {
            EventType = "not-a-real-event",
            MovieId = 1,
            EpisodeId = 1
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }


    [Fact]
    public async Task StreamController_LocalMediaStream_ServesPhysicalFileWithRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cinemaflow-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var testFilePath = Path.Combine(tempDir, "sample_real_film.mp4");
        await File.WriteAllBytesAsync(testFilePath, new byte[1024]);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:MediaDirectory"] = tempDir
                })
                .Build();

            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
            await using (var seedDb = new AppDbContext(options))
            {
                seedDb.StreamSources.Add(new StreamSourceEntity
                {
                    EpisodeId = 999,
                    Position = 1,
                    ServerName = "Local FastHost",
                    ServerType = WebXemPhim.Shared.Enums.StreamServerType.HydraCdn,
                    Quality = "1080p FHD",
                    StreamUrl = "local://sample_real_film.mp4"
                });
                await seedDb.SaveChangesAsync();
            }

            var signer = new StreamUrlSigner();
            var sessions = new Mock<IStreamSessionService>();
            var sessionId = "test-session-local";
            sessions.Setup(s => s.GetActiveLeaseAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActiveStreamDto
                {
                    EpisodeId = 999,
                    SessionId = sessionId,
                    IsActive = true
                });

            var controller = new StreamController(
                new TestDbContextFactory(options),
                signer,
                new PublicUrlResolver(config),
                new Mock<IHttpClientFactory>().Object,
                NullLogger<StreamController>.Instance,
                sessions.Object,
                configuration: config);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            var signed = signer.Sign("http://localhost", 999, 1, "127.0.0.1", sessionId: sessionId, scope: "play");
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

            var result = await controller.PlayStream(
                999,
                1,
                long.Parse(query["exp"]!),
                query["ip"],
                query["sig"],
                query["sid"],
                query["scope"],
                query["kid"],
                CancellationToken.None);

            var physicalFile = Assert.IsType<PhysicalFileResult>(result);
            Assert.True(physicalFile.EnableRangeProcessing);
            Assert.Equal("video/mp4", physicalFile.ContentType);
            Assert.Equal(testFilePath, physicalFile.FileName);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StreamController_LocalMediaStream_HeadRequest_ReturnsFileInfoHeaders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cinemaflow-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var testFilePath = Path.Combine(tempDir, "sample_real_film_head.mp4");
        await File.WriteAllBytesAsync(testFilePath, new byte[2048]);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:MediaDirectory"] = tempDir
                })
                .Build();

            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webxemphim-phase1-{Guid.NewGuid():N}").Options;
            await using (var seedDb = new AppDbContext(options))
            {
                seedDb.StreamSources.Add(new StreamSourceEntity
                {
                    EpisodeId = 998,
                    Position = 1,
                    ServerName = "Local FastHost",
                    ServerType = WebXemPhim.Shared.Enums.StreamServerType.VipFastServer,
                    Quality = "1080p FHD",
                    StreamUrl = "local://sample_real_film_head.mp4"
                });
                await seedDb.SaveChangesAsync();
            }

            var signer = new StreamUrlSigner();
            var sessions = new Mock<IStreamSessionService>();
            var sessionId = "test-session-head";
            sessions.Setup(s => s.GetActiveLeaseAsync(sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActiveStreamDto
                {
                    EpisodeId = 998,
                    SessionId = sessionId,
                    IsActive = true
                });

            var controller = new StreamController(
                new TestDbContextFactory(options),
                signer,
                new PublicUrlResolver(config),
                new Mock<IHttpClientFactory>().Object,
                NullLogger<StreamController>.Instance,
                sessions.Object,
                configuration: config);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Head;
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var signed = signer.Sign("http://localhost", 998, 1, "127.0.0.1", sessionId: sessionId, scope: "play");
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

            var result = await controller.PlayStream(
                998,
                1,
                long.Parse(query["exp"]!),
                query["ip"],
                query["sig"],
                query["sid"],
                query["scope"],
                query["kid"],
                CancellationToken.None);

            Assert.IsType<EmptyResult>(result);
            Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            Assert.Equal(2048, httpContext.Response.Headers.ContentLength);
            Assert.Equal("video/mp4", httpContext.Response.Headers.ContentType);
            Assert.Equal("bytes", httpContext.Response.Headers.AcceptRanges);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AdminEpisodesController_ProbeStream_AllowsLocalScheme()
    {
        var controller = new AdminEpisodesController(
            new Mock<IDbContextFactory<AppDbContext>>().Object,
            TimeProvider.System,
            httpClientFactory: new Mock<IHttpClientFactory>().Object);

        var result = await controller.ProbeStream(new ProbeStreamRequest
        {
            StreamUrl = "local://tears_of_steel_720p.mp4"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<ProbeStreamResultDto>>(ok.Value);
        Assert.True(apiRes.Data!.IsSuccess);
        Assert.Equal(200, apiRes.Data.StatusCode);
    }

    [Fact]
    public async Task AdminEpisodesController_ProbeStream_R2WithoutCloudStorage_Returns503()
    {
        var controller = new AdminEpisodesController(
            new Mock<IDbContextFactory<AppDbContext>>().Object,
            TimeProvider.System,
            httpClientFactory: new Mock<IHttpClientFactory>().Object);

        var result = await controller.ProbeStream(new ProbeStreamRequest
        {
            StreamUrl = "r2://webxemphim-movies/sample.mp4"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<ProbeStreamResultDto>>(ok.Value);
        Assert.False(apiRes.Data!.IsSuccess);
        Assert.Equal(503, apiRes.Data.StatusCode);
        Assert.Contains("Cloudflare R2", apiRes.Data.ErrorMessage);
    }

    [Fact]
    public async Task AdminEpisodesController_ProbeStream_R2Unconfigured_Returns503()
    {
        var mockCloud = new Mock<WebXemPhim.Server.Services.CloudStorage.ICloudStorageService>();
        mockCloud.Setup(c => c.IsCloudStorageUrl(It.IsAny<string>())).Returns(true);
        mockCloud.Setup(c => c.IsConfigured).Returns(false);

        var controller = new AdminEpisodesController(
            new Mock<IDbContextFactory<AppDbContext>>().Object,
            TimeProvider.System,
            httpClientFactory: new Mock<IHttpClientFactory>().Object,
            cloudStorage: mockCloud.Object);

        var result = await controller.ProbeStream(new ProbeStreamRequest
        {
            StreamUrl = "r2://webxemphim-movies/sample.mp4"
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiRes = Assert.IsType<ApiResponse<ProbeStreamResultDto>>(ok.Value);
        Assert.False(apiRes.Data!.IsSuccess);
        Assert.Equal(503, apiRes.Data.StatusCode);
        Assert.Contains("khóa truy cập", apiRes.Data.ErrorMessage);
    }
}
