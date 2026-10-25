using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class StreamPhase0SecurityTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private const string KeyOne = "phase0-key-one-0123456789-abcdefghijklmnopqrstuvwxyz";
    private const string KeyTwo = "phase0-key-two-0123456789-abcdefghijklmnopqrstuvwxyz";
    private const string StableIpHashKey = "phase0-ip-hash-stable-key-0123456789-abcdef";

    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private static IConfiguration BuildKeyRingConfig(string activeKid) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKeys:0:Kid"] = "k1",
            ["Security:StreamSigningKeys:0:Secret"] = KeyOne,
            ["Security:StreamSigningKeys:1:Kid"] = "k2",
            ["Security:StreamSigningKeys:1:Secret"] = KeyTwo,
            ["Security:ActiveStreamKid"] = activeKid,
            ["Security:StreamIpHashKey"] = StableIpHashKey,
            ["Security:StreamTtlMinutes"] = "10"
        }).Build();

    private static async Task<string> RegisterUserAsync(HttpClient client, string email, string name = "Phase 0 Tester")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = name,
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

    [Fact]
    public void Signer_DefaultTtl_IsTenMinutes()
    {
        var signer = new StreamUrlSigner();
        var signed = signer.Sign("https://cinemaflow.test", 101, 1, "127.0.0.1");

        Assert.Equal(600, signed.ExpiresInSeconds);
        Assert.DoesNotContain("sid=", signed.StreamUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signer_KeyRing_RotatesWithoutInvalidatingOldTokens()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var signerWithOldKey = new StreamUrlSigner(BuildKeyRingConfig("k1"), timeProvider);
        var signed = signerWithOldKey.Sign(
            "https://cinemaflow.test",
            101,
            1,
            "127.0.0.1",
            sessionId: "session-abc",
            scope: "play");

        Assert.Contains("kid=k1", signed.StreamUrl);

        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        var signerWithNewKey = new StreamUrlSigner(BuildKeyRingConfig("k2"), timeProvider);
        var oldTokenResult = signerWithNewKey.Validate(
            101, 1, exp, ip, sig, "127.0.0.1",
            sessionId: "session-abc", scope: "play", keyId: "k1");

        Assert.True(oldTokenResult.IsValid);

        var newSigned = signerWithNewKey.Sign(
            "https://cinemaflow.test",
            101,
            1,
            "127.0.0.1",
            sessionId: "session-abc",
            scope: "play");
        Assert.Contains("kid=k2", newSigned.StreamUrl);

        var unknownKeyResult = signerWithNewKey.Validate(
            101, 1, exp, ip, sig, "127.0.0.1",
            sessionId: "session-abc", scope: "play", keyId: "missing");
        Assert.False(unknownKeyResult.IsValid);
        Assert.Equal(StreamValidationStatus.UnknownKey, unknownKeyResult.Status);
    }

    [Fact]
    public void Signer_SessionAndScope_AreBoundIntoSignature()
    {
        var signer = new StreamUrlSigner();
        var signed = signer.Sign(
            "https://cinemaflow.test",
            101,
            1,
            "127.0.0.1",
            sessionId: "session-abc",
            scope: "play");

        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        Assert.Equal("session-abc", signed.SessionId);
        Assert.Equal("session-abc", query["sid"]);
        Assert.Equal("play", query["scope"]);

        Assert.True(signer.Validate(101, 1, exp, ip, sig, "127.0.0.1", sessionId: "session-abc", scope: "play").IsValid);
        Assert.False(signer.Validate(101, 1, exp, ip, sig, "127.0.0.1", sessionId: "session-other", scope: "play").IsValid);
        Assert.False(signer.Validate(101, 1, exp, ip, sig, "127.0.0.1", sessionId: "session-abc", scope: "download").IsValid);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://127.0.0.1/video.mp4")]
    [InlineData("http://localhost/video.mp4")]
    [InlineData("http://[::1]/video.mp4")]
    [InlineData("http://10.0.0.5/video.mp4")]
    [InlineData("http://172.16.0.1/video.mp4")]
    [InlineData("http://192.168.1.10/video.mp4")]
    [InlineData("http://100.64.0.1/video.mp4")]
    [InlineData("http://0.0.0.0/video.mp4")]
    [InlineData("https://example.com:5432/video.mp4")]
    [InlineData("https://user:pass@example.com/video.mp4")]
    [InlineData("ftp://example.com/video.mp4")]
    public async Task SafeStreamUrlValidator_RejectsSsrfAndDangerousTargets(string url)
    {
        var (isSafe, error, _) = await SafeStreamUrlValidator.ValidateUrlAsync(url, resolveDns: false);

        Assert.False(isSafe);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("https://example.com/video.m3u8")]
    [InlineData("https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4")]
    public async Task SafeStreamUrlValidator_AllowsPublicHostnames(string url)
    {
        var (isSafe, error, uri) = await SafeStreamUrlValidator.ValidateUrlAsync(url, resolveDns: false);

        Assert.True(isSafe, error);
        Assert.NotNull(uri);
    }

    [Fact]
    public async Task Integration_SignStream_CreatesLeaseAndBindsItToUrl()
    {
        using var client = factory.CreateClient();
        var email = $"phase0-sign-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);
        await SetUserMembershipAsync(email, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var response = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        Assert.True(payload!.Success);
        var signed = payload.Data!;
        Assert.False(string.IsNullOrWhiteSpace(signed.SessionId));

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);
        Assert.Equal(signed.SessionId, query["sid"]);
        Assert.Equal("play", query["scope"]);

        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var lease = await db.ActiveStreams.SingleAsync(s => s.SessionId == signed.SessionId);
        Assert.True(lease.IsActive);
        Assert.Equal(101, lease.EpisodeId);
    }

    [Fact]
    public async Task Integration_PlayStream_WithRevokedLease_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var email = $"phase0-revoke-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);
        await SetUserMembershipAsync(email, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var signResponse = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });
        Assert.Equal(HttpStatusCode.OK, signResponse.StatusCode);
        var payload = await signResponse.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        var signed = payload!.Data!;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);
        var sid = query["sid"]!;

        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var lease = await db.ActiveStreams.SingleAsync(s => s.SessionId == sid);
            lease.IsActive = false;
            await db.SaveChangesAsync();
        }

        var playResponse = await client.GetAsync(new Uri(signed.StreamUrl).PathAndQuery);

        Assert.Equal(HttpStatusCode.Forbidden, playResponse.StatusCode);
    }

    [Fact]
    public async Task Integration_SignStream_WithAnotherUsersSession_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var firstEmail = $"phase0-owner-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, firstEmail);
        await SetUserMembershipAsync(firstEmail, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var firstSign = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });
        var firstPayload = await firstSign.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        var firstSessionId = firstPayload!.Data!.SessionId;

        var secondEmail = $"phase0-intruder-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, secondEmail);
        await SetUserMembershipAsync(secondEmail, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var secondSign = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1,
            SessionId = firstSessionId
        });

        Assert.Equal(HttpStatusCode.Forbidden, secondSign.StatusCode);
    }
}

public sealed class StreamRateLimitSeparationTests : IClassFixture<StreamRateLimitSeparationTests.LowRateLimitFactory>
{
    private readonly LowRateLimitFactory _factory;

    public StreamRateLimitSeparationTests(LowRateLimitFactory factory) => _factory = factory;

    public sealed class LowRateLimitFactory : TestApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RateLimits:ApiPerMinute"] = "1",
                ["Security:RateLimits:StreamPlaybackPerMinute"] = "5",
                ["Security:RateLimits:StreamSignPerMinute"] = "100"
            }));
        }
    }

    [Fact]
    public async Task PlaybackEndpoint_UsesDedicatedRateLimit_NotGlobalApiLimit()
    {
        using var client = _factory.CreateClient();

        var firstApi = await client.GetAsync("/api/movies?page=1&pageSize=1");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, firstApi.StatusCode);

        var secondApi = await client.GetAsync("/api/movies?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.TooManyRequests, secondApi.StatusCode);

        const string playbackUrl = "/api/stream/play?episodeId=101&pos=1&exp=1&ip=abc&sig=def";
        for (var i = 0; i < 5; i++)
        {
            var allowed = await client.GetAsync(playbackUrl);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var blocked = await client.GetAsync(playbackUrl);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public void CinemaHub_CanBeActivatedByDependencyInjection()
    {
        // CinemaHub có constructor không tham số (dùng trong unit test) và constructor DI.
        // Nếu thiếu [ActivatorUtilitiesConstructor], SignalR sẽ ném lỗi "Multiple constructors".
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IStreamSessionService>().Object);

        using var provider = services.BuildServiceProvider();
        var hub = ActivatorUtilities.CreateInstance<CinemaHub>(provider);

        Assert.NotNull(hub);
    }
}
