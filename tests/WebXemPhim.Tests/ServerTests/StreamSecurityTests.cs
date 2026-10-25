using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class StreamSecurityTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static async Task<string> RegisterUserAsync(HttpClient client, string email, string name = "VIP Stream Tester")
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

    private async Task SetUserMembershipAsync(string email, MembershipTier tier, DateTime? expiresAtUtc, bool isAdmin = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Membership = tier;
        user.VipExpiresAtUtc = expiresAtUtc;
        if (isAdmin)
        {
            var sec = await db.UserSecurity.SingleOrDefaultAsync(s => s.UserId == user.Id);
            if (sec is null)
            {
                sec = new UserSecurityEntity { UserId = user.Id, IsAdmin = true };
                db.UserSecurity.Add(sec);
            }
            else
            {
                sec.IsAdmin = true;
            }
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Unit_Sign_GeneratesValidSignatureAndFormattedPlaybackUrl()
    {
        var signer = new StreamUrlSigner();
        var signed = signer.Sign("https://cinemaflow.test", 101, 1, "192.168.1.10", TimeSpan.FromMinutes(60));

        Assert.NotNull(signed);
        Assert.StartsWith("https://cinemaflow.test/api/stream/play?", signed.StreamUrl);
        Assert.Contains("episodeId=101", signed.StreamUrl);
        Assert.Contains("pos=1", signed.StreamUrl);
        Assert.Contains("exp=", signed.StreamUrl);
        Assert.Contains("ip=", signed.StreamUrl);
        Assert.Contains("sig=", signed.StreamUrl);
        Assert.Equal(3600, signed.ExpiresInSeconds);
    }

    [Fact]
    public void Unit_Validate_WhenValidSignatureAndMatchingIp_ReturnsSuccess()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var signer = new StreamUrlSigner(timeProvider: timeProvider);
        var clientIp = "14.161.22.88";

        var signed = signer.Sign("https://cinemaflow.test", 202, 2, clientIp, TimeSpan.FromMinutes(30));
        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var episodeId = int.Parse(query["episodeId"]!);
        var pos = int.Parse(query["pos"]!);
        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        var result = signer.Validate(episodeId, pos, exp, ip, sig, clientIp);
        Assert.True(result.IsValid);
        Assert.Equal(StreamValidationStatus.Valid, result.Status);
    }

    [Fact]
    public void Unit_Validate_WhenExpired_ReturnsExpiredStatus()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var signer = new StreamUrlSigner(timeProvider: timeProvider);
        var clientIp = "127.0.0.1";

        var signed = signer.Sign("https://cinemaflow.test", 101, 1, clientIp, TimeSpan.FromMinutes(10));
        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        // Advance time past expiry
        timeProvider.Advance(TimeSpan.FromMinutes(11));

        var result = signer.Validate(101, 1, exp, ip, sig, clientIp);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.Expired, result.Status);
        Assert.Contains("hết hạn", result.ErrorMessage);
    }

    [Fact]
    public void Unit_Validate_WhenTamperedSignature_ReturnsInvalidSignatureStatus()
    {
        var signer = new StreamUrlSigner();
        var clientIp = "127.0.0.1";

        var signed = signer.Sign("https://cinemaflow.test", 101, 1, clientIp);
        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        // Tamper with signature
        var tamperedSig = sig.EndsWith('a') ? sig[..^1] + "b" : sig[..^1] + "a";

        var result = signer.Validate(101, 1, exp, ip, tamperedSig, clientIp);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.InvalidSignature, result.Status);
        Assert.Contains("can thiệp", result.ErrorMessage);
    }

    [Fact]
    public void Unit_Validate_WhenIpMismatched_ReturnsIpMismatchStatus()
    {
        var signer = new StreamUrlSigner();
        var clientIp = "118.69.182.10";
        var differentIp = "42.112.98.5";

        var signed = signer.Sign("https://cinemaflow.test", 101, 1, clientIp);
        var uri = new Uri(signed.StreamUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var exp = long.Parse(query["exp"]!);
        var ip = query["ip"]!;
        var sig = query["sig"]!;

        var result = signer.Validate(101, 1, exp, ip, sig, differentIp);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.IpMismatch, result.Status);
        Assert.Contains("không khớp", result.ErrorMessage);
    }

    [Fact]
    public async Task Integration_PlayStream_WithValidSignature_ProxiesVideoWithoutLeakingUpstreamUrl()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var email = $"vip-stream-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);
        await SetUserMembershipAsync(email, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        // Sign stream for episode 101, position 1
        var signRes = await client.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });
        Assert.Equal(HttpStatusCode.OK, signRes.StatusCode);

        var signPayload = await signRes.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        Assert.True(signPayload!.Success);
        Assert.NotNull(signPayload.Data);

        var playbackUri = new Uri(signPayload.Data.StreamUrl);
        var relativeUrl = playbackUri.PathAndQuery;

        // Play stream via GET /api/stream/play
        var playRes = await client.GetAsync(relativeUrl);

        // Video được PROXY qua server chứ không chuyển hướng. Nếu chuyển hướng, URL CDN gốc
        // lộ ra trong header Location và dùng được vô thời hạn — cơ chế ký, giới hạn thời
        // hạn và ràng buộc IP sẽ mất tác dụng.
        Assert.NotEqual(HttpStatusCode.Found, playRes.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, playRes.StatusCode);
        Assert.Null(playRes.Headers.Location);

        // Không được lộ URL máy chủ nguồn ở bất kỳ header nào.
        foreach (var header in playRes.Headers)
        {
            Assert.DoesNotContain("cdn", header.Value.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".mp4", header.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Nội dung trả về phải là video (hoặc mã lỗi rõ ràng của máy chủ nguồn) và tuyệt đối
        // không phải trang HTML lỗi.
        Assert.True(playRes.StatusCode == HttpStatusCode.OK || playRes.StatusCode == HttpStatusCode.PartialContent
            || playRes.StatusCode == HttpStatusCode.BadGateway, $"Unexpected status {(int)playRes.StatusCode}.");
        Assert.DoesNotContain("<html", await playRes.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Integration_PlayStream_WithExpiredSignature_Returns403Forbidden()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var expiredUnix = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var sig = signer.ComputeSignature(101, 1, expiredUnix, ipHash);

        var expiredUrl = $"/api/stream/play?episodeId=101&pos=1&exp={expiredUnix}&ip={ipHash}&sig={sig}";
        var response = await client.GetAsync(expiredUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Integration_PlayStream_WithAlteredSignatureOrTamperedQuery_Returns403Forbidden()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var validUnix = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var validSig = signer.ComputeSignature(101, 1, validUnix, ipHash);

        // 1. Altered signature
        var tamperedSig = validSig.EndsWith('f') ? validSig[..^1] + "0" : validSig[..^1] + "f";
        var tamperedSigUrl = $"/api/stream/play?episodeId=101&pos=1&exp={validUnix}&ip={ipHash}&sig={tamperedSig}";
        var res1 = await client.GetAsync(tamperedSigUrl);
        Assert.Equal(HttpStatusCode.Forbidden, res1.StatusCode);

        // 2. Tampered episodeId
        var tamperedEpUrl = $"/api/stream/play?episodeId=999&pos=1&exp={validUnix}&ip={ipHash}&sig={validSig}";
        var res2 = await client.GetAsync(tamperedEpUrl);
        Assert.Equal(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [Fact]
    public async Task Integration_PlayStream_WithMismatchedIp_Returns403Forbidden()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        var validUnix = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds();
        // Bind to a foreign IP hash
        var foreignIpHash = signer.ComputeIpHash("203.113.131.2");
        var sig = signer.ComputeSignature(101, 1, validUnix, foreignIpHash);

        var url = $"/api/stream/play?episodeId=101&pos=1&exp={validUnix}&ip={foreignIpHash}&sig={sig}";
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Integration_SignStream_WithNonVipOrUnauthenticated_ReturnsForbiddenOrUnauthorized()
    {
        using var guestClient = factory.CreateClient();

        // 1. Unauthenticated -> 401
        var unauthRes = await guestClient.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Free user -> 403
        using var freeClient = factory.CreateClient();
        var email = $"free-stream-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(freeClient, email);
        // By default, registration gives Free membership

        var freeRes = await freeClient.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = 101,
            Position = 1
        });
        Assert.Equal(HttpStatusCode.Forbidden, freeRes.StatusCode);
    }

    [Fact]
    public async Task Integration_PlayStream_WhenNonVipUserAccessesVipStream_Returns403Forbidden()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Register free user
        var email = $"free-play-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);

        using var scope = factory.Services.CreateScope();
        var signer = scope.ServiceProvider.GetRequiredService<IStreamUrlSigner>();

        // Generate signature for episode 101, pos 1 (VIP Fast Server stream)
        var validUnix = DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds();
        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var sig = signer.ComputeSignature(101, 1, validUnix, ipHash);

        var url = $"/api/stream/play?episodeId=101&pos=1&exp={validUnix}&ip={ipHash}&sig={sig}";
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Integration_MaskVipStreams_ForVipUser_RequiresSignEndpointForPlaybackUrl()
    {
        using var vipClient = factory.CreateClient();
        var email = $"vip-mask-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(vipClient, email);
        await SetUserMembershipAsync(email, MembershipTier.Vip, DateTime.UtcNow.AddDays(30));

        var res = (await vipClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

        var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer ||
                                         (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.NotEmpty(vipAnd4k);
        // Verified: VIP streams do not expose pre-signed URLs without sid; they require /api/stream/sign
        Assert.All(vipAnd4k, s => Assert.Equal(string.Empty, s.StreamUrl));

        // Signing through /api/stream/sign grants the signed URL bound to a session lease
        var firstVip = vipAnd4k.First();
        var signRes = await vipClient.PostAsJsonAsync("/api/stream/sign", new SignStreamRequest
        {
            EpisodeId = firstVip.EpisodeId,
            Position = firstVip.Position
        });
        signRes.EnsureSuccessStatusCode();
        var signed = (await signRes.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>())!.Data!;
        Assert.NotNull(signed);
        Assert.False(string.IsNullOrWhiteSpace(signed.SessionId));
        Assert.StartsWith("http", signed.StreamUrl);
        Assert.Contains("/api/stream/play?", signed.StreamUrl);
        Assert.Contains("sig=", signed.StreamUrl);
        Assert.Contains("exp=", signed.StreamUrl);
        Assert.Contains("ip=", signed.StreamUrl);
        Assert.Contains($"sid={signed.SessionId}", signed.StreamUrl);
        Assert.DoesNotContain("cdn.plyr.io", signed.StreamUrl);
    }

    [Fact]
    public async Task Integration_MaskVipStreams_ForNonVipUser_ReturnsEmptyString()
    {
        using var guestClient = factory.CreateClient();

        var res = (await guestClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

        var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer ||
                                         (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.NotEmpty(vipAnd4k);
        Assert.All(vipAnd4k, s => Assert.True(string.IsNullOrEmpty(s.StreamUrl)));
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
