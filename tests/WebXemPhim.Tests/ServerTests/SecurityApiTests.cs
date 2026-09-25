using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class SecurityApiTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Theory]
    [InlineData("stats")]
    [InlineData("users")]
    [InlineData("payments")]
    [InlineData("reviews")]
    [InlineData("movies")]
    public async Task AdminDataRejectsGuestsAndRegularMembers(string route)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/" + route)).StatusCode);
        var auth = await Register(client);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/" + route)).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/admin/movies")]
    [InlineData("PUT", "/api/admin/movies/1")]
    [InlineData("DELETE", "/api/admin/movies/1")]
    [InlineData("DELETE", "/api/admin/episodes/1")]
    [InlineData("PUT", "/api/admin/users/11111111-1111-1111-1111-111111111111/membership")]
    public async Task AdminMutationsRejectRegularMembers(string method, string path)
    {
        using var client = factory.CreateClient();
        Authorize(client, (await Register(client)).AccessToken);
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { Title = "Not authorized" }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task AdminLookingEmailAndExtraRoleFieldsCannotGrantPrivileges()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            FullName = "Not an administrator", Email = $"admin@{Guid.NewGuid():N}.test", Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true,
            IsAdmin = true, Membership = 1, Role = "Admin"
        });
        var auth = (await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Assert.False(auth.User.IsAdmin);
        Assert.Equal(WebXemPhim.Shared.Enums.MembershipTier.Free, auth.User.Membership);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/stats")).StatusCode);
    }

    [Fact]
    public async Task ServerStoredRoleAllowsAdminAndRevocationTakesEffectImmediately()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        await SetAdmin(auth.User.Id, true);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);
        await SetAdmin(auth.User.Id, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task TokensAreHashedAndLogoutRevokesOnlyCurrentSession()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = auth.User.Email, Password = "Cinema123" });
        var second = (await login.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        await using var db = await Db();
        var tokens = await db.AccessTokens.Where(item => item.UserId == auth.User.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, item => Assert.StartsWith("sha256:", item.Token));
        Assert.DoesNotContain(tokens, item => item.Token == auth.AccessToken || item.Token == second.AccessToken);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Authorize(client, second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PlaintextLegacySessionIsRejected()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        var legacy = CredentialSecurity.CreateToken();
        await using var db = await Db();
        db.AccessTokens.Add(new AccessTokenEntity { Token = legacy, UserId = auth.User.Id, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        Authorize(client, legacy);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordResetDoesNotReturnTokenAndRevokesAllSessionsAndOtherResets()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        var sender = (TestResetEmailSender)factory.Services.GetRequiredService<IPasswordResetEmailSender>();
        var first = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = auth.User.Email });
        var payload = await first.Content.ReadFromJsonAsync<ApiResponse<PasswordResetDto?>>();
        Assert.Null(payload!.Data);
        var oldReset = sender.Tokens[auth.User.Email];
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = auth.User.Email });
        var token = sender.Tokens[auth.User.Email];
        await using var db = await Db();
        Assert.All(await db.PasswordResetTokens.Where(item => item.UserId == auth.User.Id).ToListAsync(), item => Assert.StartsWith("sha256:", item.Token));
        var reset = new ResetPasswordRequest { Token = token, NewPassword = "NewCinema456", ConfirmPassword = "NewCinema456" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset-password", reset)).StatusCode);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        reset.Token = oldReset;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", reset)).StatusCode);
    }

    [Fact]
    public async Task FailedLoginLocksAccountWithoutRevealingReason()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = auth.User.Email, Password = "Wrong123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = auth.User.Email, Password = "Cinema123" })).StatusCode);
        await using var db = await Db();
        var row = await db.UserSecurity.SingleAsync(item => item.UserId == auth.User.Id);
        Assert.Equal(5, row.FailedLoginAttempts);
        Assert.True(row.LockedUntilUtc > DateTime.UtcNow);
        row.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = auth.User.Email, Password = "Cinema123" })).StatusCode);
    }

    [Fact]
    public async Task GuestLegacyWatchlistIsProtectedAndAliasIsUserScoped()
    {
        using var a = factory.CreateClient(); using var b = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/watchlist")).StatusCode);
        Authorize(a, (await Register(a)).AccessToken); Authorize(b, (await Register(b)).AccessToken);
        await a.PostAsync("/api/watchlist/1", null);
        var result = await b.GetFromJsonAsync<ApiResponse<List<WatchlistItemDto>>>("/api/watchlist");
        Assert.Empty(result!.Data!);
    }

    [Fact]
    public async Task CorsDoesNotTrustArbitraryOrigin()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/movies");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task PrivateResponsesCannotBeCachedAndHaveBrowserSecurityHeaders()
    {
        using var client = factory.CreateClient();
        Authorize(client, (await Register(client)).AccessToken);
        var response = await client.GetAsync("/api/auth/me");
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task AdminRejectsUnsafeMediaUrl()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client); await SetAdmin(auth.User.Id, true); Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/movies", new UpsertMovieRequest { Title = "Bad media", PosterUrl = "javascript:alert(1)" })).StatusCode);
    }

    [Fact]
    public async Task OversizedLoginFieldIsRejectedBeforeHashing()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = "a@example.com", Password = new string('a', 101) })).StatusCode);
    }

    [Fact]
    public async Task AuthRateLimitReturns429AndRetryAfter()
    {
        await using var limited = new TestApiFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:RateLimits:AuthPerMinute"] = "2" })));
        using var client = limited.CreateClient();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task MfaSetupProtectsSecretAndRequiresSecondFactorAtLogin()
    {
        using var client = factory.CreateClient();
        var registered = await Register(client);
        await SetAdmin(registered.User.Id, true);
        var oldLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = registered.User.Email, Password = "Cinema123" });
        var oldUnverifiedSession = (await oldLoginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Authorize(client, registered.AccessToken);

        var setupResponse = await client.PostAsJsonAsync("/api/auth/mfa/setup", new BeginMfaSetupRequest { CurrentPassword = "Cinema123" });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = (await setupResponse.Content.ReadFromJsonAsync<ApiResponse<MfaSetupDto>>())!.Data!;
        Assert.StartsWith("otpauth://totp/", setup.AuthenticatorUri);
        var secret = DecodeBase32(setup.ManualKey);
        var enabledResponse = await client.PostAsJsonAsync("/api/auth/mfa/enable", new EnableMfaRequest { Code = CurrentTotp(secret) });
        Assert.Equal(HttpStatusCode.OK, enabledResponse.StatusCode);
        var recovery = (await enabledResponse.Content.ReadFromJsonAsync<ApiResponse<MfaRecoveryCodesDto>>())!.Data!;
        Assert.Equal(8, recovery.RecoveryCodes.Count);
        Authorize(client, oldUnverifiedSession.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);

        await using (var db = await Db())
        {
            var stored = await db.MfaCredentials.SingleAsync(item => item.UserId == registered.User.Id);
            Assert.True(stored.Enabled);
            Assert.DoesNotContain(setup.ManualKey, stored.ProtectedSecret, StringComparison.Ordinal);
            Assert.DoesNotContain(recovery.RecoveryCodes[0], stored.RecoveryCodeHashes, StringComparison.Ordinal);
        }

        client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = registered.User.Email, Password = "Cinema123" });
        var pending = (await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Assert.True(pending.RequiresMfa);
        Assert.Empty(pending.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/mfa/verify-login", new VerifyMfaLoginRequest { ChallengeToken = pending.MfaChallengeToken, Code = "000000" })).StatusCode);
        var verifiedResponse = await client.PostAsJsonAsync("/api/auth/mfa/verify-login", new VerifyMfaLoginRequest { ChallengeToken = pending.MfaChallengeToken, Code = CurrentTotp(secret) });
        Assert.Equal(HttpStatusCode.OK, verifiedResponse.StatusCode);
        var verified = (await verifiedResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Assert.False(verified.RequiresMfa);
        Assert.NotEmpty(verified.AccessToken);
        Assert.True(verified.User.MfaEnabled);
        Authorize(client, verified.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/users")).StatusCode);

        await using var auditDb = await Db();
        Assert.Contains(await auditDb.SecurityAuditEvents.ToListAsync(), item => item.UserId == registered.User.Id && item.EventType == "mfa.enable" && item.Success);
    }

    [Fact]
    public void TotpVerificationUsesStandardTimeWindowAndRejectsBadCodes()
    {
        var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.True(TotpSecurity.Verify(secret, "287082", DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime));
        Assert.False(TotpSecurity.Verify(secret, "287083", DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime));
    }

    [Fact]
    public async Task LockedUserSessionIsRejectedImmediately()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        Authorize(client, auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Lock the account in database
        await using var db = await Db();
        var security = await db.UserSecurity.SingleAsync(s => s.UserId == auth.User.Id);
        security.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
        await db.SaveChangesAsync();

        // Active session token must now be rejected immediately
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AdminLockingUserRevokesTokensAndBlocksRequests()
    {
        using var adminClient = factory.CreateClient();
        var adminAuth = await Register(adminClient);
        await SetAdmin(adminAuth.User.Id, true);
        Authorize(adminClient, adminAuth.AccessToken);

        using var userClient = factory.CreateClient();
        var userAuth = await Register(userClient);
        Authorize(userClient, userAuth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/auth/me")).StatusCode);

        // Admin locks user
        var lockRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{userAuth.User.Id}/lock-status", new
        {
            IsLocked = true,
            LockMinutes = 30
        });
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        // Tokens should be revoked and user rejected
        await using var db = await Db();
        Assert.Empty(await db.AccessTokens.Where(t => t.UserId == userAuth.User.Id).ToListAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AdminCannotLockOrDemoteSelf()
    {
        using var adminClient = factory.CreateClient();
        var adminAuth = await Register(adminClient);
        await SetAdmin(adminAuth.User.Id, true);
        Authorize(adminClient, adminAuth.AccessToken);

        // Try self-lock
        var lockRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{adminAuth.User.Id}/lock-status", new
        {
            IsLocked = true,
            LockMinutes = 15
        });
        Assert.Equal(HttpStatusCode.BadRequest, lockRes.StatusCode);

        // Try self-demote
        var demoteRes = await adminClient.PutAsJsonAsync($"/api/admin/users/{adminAuth.User.Id}/admin-role", new
        {
            IsAdmin = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, demoteRes.StatusCode);
    }

    [Fact]
    public async Task AdminProbeStreamRejectsLoopbackAndPrivateIps()
    {
        using var adminClient = factory.CreateClient();
        var adminAuth = await Register(adminClient);
        await SetAdmin(adminAuth.User.Id, true);
        Authorize(adminClient, adminAuth.AccessToken);

        var res = await adminClient.PostAsJsonAsync("/api/admin/streams/probe", new ProbeStreamRequest
        {
            StreamUrl = "https://127.0.0.1:8080/stream.m3u8"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var envelope = await res.Content.ReadFromJsonAsync<ApiResponse<ProbeStreamResultDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        Assert.False(envelope.Data.IsSuccess);
        Assert.Equal(400, envelope.Data.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordSucceedsAndInvalidatesOtherSessions()
    {
        using var clientA = factory.CreateClient();
        var authA = await Register(clientA);
        Authorize(clientA, authA.AccessToken);

        // Login on device B
        using var clientB = factory.CreateClient();
        var loginResponse = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "Cinema123"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authB = (await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Authorize(clientB, authB.AccessToken);

        // Both device A and device B can access /api/auth/me
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/api/auth/me")).StatusCode);

        // Device A changes password
        var changeResponse = await clientA.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "Cinema123",
            NewPassword = "NewCinema456",
            ConfirmPassword = "NewCinema456"
        });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        // Device A session remains valid
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/auth/me")).StatusCode);

        // Device B session is immediately revoked!
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientB.GetAsync("/api/auth/me")).StatusCode);

        // Old password no longer works
        var oldLogin = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "Cinema123"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        // New password works
        var newLogin = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = authA.User.Email,
            Password = "NewCinema456"
        });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordRejectsWrongCurrentPassword()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        Authorize(client, auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword999",
            NewPassword = "NewCinema456",
            ConfirmPassword = "NewCinema456"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Old password still works
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = auth.User.Email,
            Password = "Cinema123"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task AdminGrantVipDisambiguatesDuplicateNames()
    {
        using var client = factory.CreateClient();
        var admin = await Register(client);
        await SetAdmin(admin.User.Id, true);
        Authorize(client, admin.AccessToken);

        // Create two users with the exact same full name
        var user1Email = $"dup1-{Guid.NewGuid():N}@example.com";
        var user2Email = $"dup2-{Guid.NewGuid():N}@example.com";
        var dupName = "John Duplicate";

        using var client1 = factory.CreateClient();
        await client1.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = dupName, Email = user1Email, Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true
        });

        using var client2 = factory.CreateClient();
        await client2.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = dupName, Email = user2Email, Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true
        });

        // Attempting to grant VIP by duplicate full name should be rejected with 400
        var dupResponse = await client.PostAsJsonAsync("/api/admin/users/grant-vip", new ManualVipGrantRequest
        {
            UserEmailOrUsername = dupName,
            Days = 30
        });
        Assert.Equal(HttpStatusCode.BadRequest, dupResponse.StatusCode);

        // Granting by exact email should succeed
        var exactResponse = await client.PostAsJsonAsync("/api/admin/users/grant-vip", new ManualVipGrantRequest
        {
            UserEmailOrUsername = user1Email,
            Days = 30
        });
        Assert.Equal(HttpStatusCode.OK, exactResponse.StatusCode);
    }

    [Fact]
    public async Task LogoutWithMissingOrMalformedAuthorizationDoesNotThrowException()
    {
        using var client = factory.CreateClient();
        var auth = await Register(client);
        Authorize(client, auth.AccessToken);

        // Clear or set non-bearer authorization
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "InvalidHeaderValue");

        var response = await client.PostAsync("/api/auth/logout", null);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.OK);
    }

    private Task<AppDbContext> Db() => factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
    private async Task SetAdmin(Guid id, bool allowed)
    {
        await using var db = await Db();
        var row = await db.UserSecurity.SingleAsync(item => item.UserId == id); row.IsAdmin = allowed; row.Version++; await db.SaveChangesAsync();
    }
    private static void Authorize(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static async Task<AuthDto> Register(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { FullName = "Security Test", Email = $"security-{Guid.NewGuid():N}@example.com", Password = "Cinema123", ConfirmPassword = "Cinema123", AcceptTerms = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
    }

    private static string CurrentTotp(byte[] secret)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, counter);
        var hash = HMACSHA1.HashData(secret, bytes);
        var offset = hash[^1] & 15;
        var value = ((hash[offset] & 127) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var character in value)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character); bits += 5;
            if (bits >= 8) { output.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return output.ToArray();
    }
}
