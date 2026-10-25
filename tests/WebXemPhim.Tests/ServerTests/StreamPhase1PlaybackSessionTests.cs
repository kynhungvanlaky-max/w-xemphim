using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// P1-01: phiên phát có thể gia hạn token và thu hồi ngay; cookie HttpOnly giữ
/// cho video đang phát tiếp tục chạy khi URL ngắn hạn hết hạn.
/// </summary>
public sealed class StreamPhase1PlaybackSessionTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private const int EpisodeId = 101;
    private const int Position = 1;

    private static async Task<string> RegisterUserAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Phase 1 Tester",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = payload!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private async Task SetUserMembershipAsync(string email, MembershipTier tier, DateTime? expiresAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Membership = tier;
        user.VipExpiresAtUtc = expiresAtUtc;
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, SignedStreamDto Signed)> SignStreamAsync(string email)
    {
        var client = factory.CreateClient();
        await RegisterUserAsync(client, email);
        await SetUserMembershipAsync(email, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var response = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = EpisodeId,
            Position = Position
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        Assert.True(payload!.Success);
        Assert.False(string.IsNullOrWhiteSpace(payload.Data!.SessionId));
        return (client, payload.Data);
    }

    private static HttpRequestMessage RefreshRequest(string sessionId) =>
        new(HttpMethod.Post, $"/api/stream/sessions/{sessionId}/refresh")
        {
            Content = JsonContent.Create(new RefreshStreamRequest
            {
                EpisodeId = EpisodeId,
                Position = Position
            })
        };

    [Fact]
    public async Task Refresh_WithActiveLease_ReturnsNewUrlBoundToSameSession()
    {
        var (client, signed) = await SignStreamAsync($"phase1-refresh-{Guid.NewGuid():N}@test.vn");
        using var _ = client;

        using var response = await client.SendAsync(RefreshRequest(signed.SessionId!));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        Assert.True(payload!.Success);
        var refreshed = payload.Data!;
        Assert.Equal(signed.SessionId, refreshed.SessionId);

        var query = HttpUtility.ParseQueryString(new Uri(refreshed.StreamUrl).Query);
        Assert.Equal(signed.SessionId, query["sid"]);
        Assert.Equal("play", query["scope"]);
        Assert.True(refreshed.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Refresh_WithAnotherUsersSession_ReturnsForbidden()
    {
        var (ownerClient, signed) = await SignStreamAsync($"phase1-owner-{Guid.NewGuid():N}@test.vn");
        using var _owner = ownerClient;

        var intruderEmail = $"phase1-intruder-{Guid.NewGuid():N}@test.vn";
        var intruderClient = factory.CreateClient();
        using var _intruder = intruderClient;
        await RegisterUserAsync(intruderClient, intruderEmail);
        await SetUserMembershipAsync(intruderEmail, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        using var response = await intruderClient.SendAsync(RefreshRequest(signed.SessionId!));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterRevoke_ReturnsForbidden()
    {
        var (client, signed) = await SignStreamAsync($"phase1-revoked-{Guid.NewGuid():N}@test.vn");
        using var _ = client;

        using var revoke = await client.PostAsync($"/api/stream/sessions/{signed.SessionId}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        using var refresh = await client.SendAsync(RefreshRequest(signed.SessionId!));
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
    }

    [Fact]
    public async Task Revoke_IsIdempotent_AndMarksLeaseInactive()
    {
        var (client, signed) = await SignStreamAsync($"phase1-idempotent-{Guid.NewGuid():N}@test.vn");
        using var _ = client;

        using var first = await client.PostAsync($"/api/stream/sessions/{signed.SessionId}/revoke", null);
        using var second = await client.PostAsync($"/api/stream/sessions/{signed.SessionId}/revoke", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var lease = await db.ActiveStreams.SingleAsync(s => s.SessionId == signed.SessionId);
        Assert.False(lease.IsActive);
    }
}

/// <summary>
/// Kiểm thử trực tiếp controller cho nhánh cookie phiên phát, tránh gọi mạng thật.
/// </summary>
public sealed class StreamPlaybackCookieTests
{
    private const int EpisodeId = 4242;
    private const int Position = 1;
    private const string SessionId = "p1-cookie-session";
    private const string ClientIp = "127.0.0.1";
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class StubStreamHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            var content = new ByteArrayContent([0x00, 0x01, 0x02, 0x03]);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = content
            });
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static async Task<DbContextOptions<AppDbContext>> SeedStreamAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-p1-cookie-{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        db.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = EpisodeId,
            Position = Position,
            ServerName = "Test",
            ServerType = StreamServerType.HydraCdn,
            Quality = "1080p",
            StreamUrl = "http://1.1.1.1/public.mp4"
        });
        await db.SaveChangesAsync();
        return options;
    }

    private static (StreamController Controller, Mock<IStreamSessionService> Sessions) CreateController(
        DbContextOptions<AppDbContext> options,
        HttpClient client,
        bool leaseActive = true)
    {
        var sessions = new Mock<IStreamSessionService>();
        sessions
            .Setup(s => s.GetActiveLeaseAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseActive
                ? new ActiveStreamDto
                {
                    UserId = UserId,
                    EpisodeId = EpisodeId,
                    SessionId = SessionId,
                    IsActive = true
                }
                : null);

        var controller = new StreamController(
            new TestDbContextFactory(options),
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new SingleClientFactory(client),
            NullLogger<StreamController>.Instance,
            sessions.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
        return (controller, sessions);
    }

    private static Task<IActionResult> PlayAsync(
        StreamController controller,
        string streamUrl,
        string? cookie = null)
    {
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            controller.Request.Headers["Cookie"] = $"{StreamPlaybackCookie.Name}={cookie}";
        }

        var query = HttpUtility.ParseQueryString(new Uri(streamUrl).Query);
        return controller.PlayStream(
            EpisodeId,
            Position,
            long.Parse(query["exp"]!),
            query["ip"],
            query["sig"],
            query["sid"],
            query["scope"],
            query["kid"],
            CancellationToken.None);
    }

    private static string CreateExpiredUrl(StreamUrlSigner signer) =>
        signer.Sign(
            "http://localhost",
            EpisodeId,
            Position,
            ClientIp,
            TimeSpan.FromMinutes(-1),
            sessionId: SessionId,
            scope: "play").StreamUrl;

    [Fact]
    public void Cookie_ValidForMatchingSessionEpisodeAndIp()
    {
        var signer = new StreamUrlSigner();
        var cookie = StreamPlaybackCookie.Create(
            signer, SessionId, EpisodeId, ClientIp, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

        Assert.True(StreamPlaybackCookie.Validate(signer, cookie, SessionId, EpisodeId, ClientIp));
        Assert.False(StreamPlaybackCookie.Validate(signer, cookie, "other-session", EpisodeId, ClientIp));
        Assert.False(StreamPlaybackCookie.Validate(signer, cookie, SessionId, EpisodeId + 1, ClientIp));
        Assert.False(StreamPlaybackCookie.Validate(signer, cookie, SessionId, EpisodeId, "10.0.0.1"));
    }

    [Fact]
    public void Cookie_ExpiredOrTampered_IsRejected()
    {
        var signer = new StreamUrlSigner();
        var expired = StreamPlaybackCookie.Create(
            signer, SessionId, EpisodeId, ClientIp, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(-1));
        var tampered = expired[..^1] + (expired[^1] == 'A' ? 'B' : 'A');

        Assert.False(StreamPlaybackCookie.Validate(signer, expired, SessionId, EpisodeId, ClientIp));
        Assert.False(StreamPlaybackCookie.Validate(signer, tampered, SessionId, EpisodeId, ClientIp));
    }

    [Fact]
    public async Task ExpiredUrl_WithValidCookie_ContinuesPlayback()
    {
        var options = await SeedStreamAsync();
        using var client = new HttpClient(new StubStreamHandler());
        var (controller, _) = CreateController(options, client);
        var signer = new StreamUrlSigner();
        var cookie = StreamPlaybackCookie.Create(
            signer, SessionId, EpisodeId, ClientIp, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

        var result = await PlayAsync(controller, CreateExpiredUrl(signer), cookie);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal(StatusCodes.Status206PartialContent, controller.Response.StatusCode);
        Assert.True(controller.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task ExpiredUrl_WithoutCookie_ReturnsForbidden()
    {
        var options = await SeedStreamAsync();
        using var client = new HttpClient(new StubStreamHandler());
        var (controller, _) = CreateController(options, client);
        var signer = new StreamUrlSigner();

        var result = await PlayAsync(controller, CreateExpiredUrl(signer));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task ExpiredUrl_WithTamperedCookie_ReturnsForbidden()
    {
        var options = await SeedStreamAsync();
        using var client = new HttpClient(new StubStreamHandler());
        var (controller, _) = CreateController(options, client);
        var signer = new StreamUrlSigner();
        var cookie = StreamPlaybackCookie.Create(
            signer, SessionId, EpisodeId, ClientIp, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
        var tampered = cookie[..^1] + (cookie[^1] == 'A' ? 'B' : 'A');

        var result = await PlayAsync(controller, CreateExpiredUrl(signer), tampered);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task ExpiredUrl_WithValidCookieButRevokedLease_ReturnsForbidden()
    {
        var options = await SeedStreamAsync();
        using var client = new HttpClient(new StubStreamHandler());
        var (controller, _) = CreateController(options, client, leaseActive: false);
        var signer = new StreamUrlSigner();
        var cookie = StreamPlaybackCookie.Create(
            signer, SessionId, EpisodeId, ClientIp, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));

        var result = await PlayAsync(controller, CreateExpiredUrl(signer), cookie);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }
}
