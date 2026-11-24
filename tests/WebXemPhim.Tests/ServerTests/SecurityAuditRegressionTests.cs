using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Server.Services.CloudStorage;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using WebXemPhim.Shared.Security;
using WebXemPhim.Shared.Validation;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Kiểm thử hồi quy bảo mật cho các lỗ hổng đã phát hiện và khắc phục trong đợt Security Audit 2026:
/// - Tách brute-force LoginCooldown khỏi Admin LockedUntilUtc (chống DoS phiên Admin)
/// - Ký HMAC-SHA256 địa chỉ IP người dùng qua reverse proxy/NAT động
/// - Khóa phân luồng tĩnh chống race condition AcquireLeaseAsync
/// - Chặn SSRF IPv4-compatible IPv6 và bypass IP mismatch trên StreamController
/// - Chặn upload tệp HTML/SVG/JS và path traversal trên R2 storage
/// - Chặn encoded traversal / CSS breakout / metadata SSRF trong SafeResourceUrlAttribute
/// - Mã hoá AES-256-GCM khoá DataProtection lưu trên PostgreSQL
/// - Fail-closed webhook secret ở Staging và bắt buộc Timestamp + TransactionReference ở Production
/// </summary>
public sealed class SecurityAuditRegressionTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private sealed class DummyCloudStorage : ICloudStorageService
    {
        public bool IsConfigured => true;
        public bool IsCloudStorageUrl(string? streamUrl) => false;
        public string ExtractObjectKey(string streamUrl) => streamUrl;
        public Task<string?> ResolvePlaybackUrlAsync(string streamUrl, TimeSpan? expiry = null, string httpMethod = "GET", CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task<(Stream? ContentStream, string? ContentType, long ContentLength, bool SupportsRange)> OpenReadStreamAsync(string objectKey, string? rangeHeader = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<(Stream?, string?, long, bool)>((null, null, 0, false));
        public Task<string> UploadFileAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult($"r2://bucket/{objectKey}");
        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<CloudStoragePresignedUploadDto?> GeneratePresignedUploadUrlAsync(string fileName, string? contentType = null, string folder = "episodes", TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<CloudStoragePresignedUploadDto?>(new CloudStoragePresignedUploadDto
            {
                UploadUrl = "https://r2.example.test/upload",
                ObjectKey = $"{folder}/202611/{fileName}",
                R2Url = $"r2://bucket/{folder}/{fileName}",
                ContentType = contentType ?? "video/mp4",
                ExpiresInMinutes = 120
            });
    }

    [Fact]
    public async Task BruteForceLogin_CooldownDoesNotRevokeActiveSession_WhileAdminLockRevokesImmediately()
    {
        using var client = factory.CreateClient();
        var email = $"audit-lockout-{Guid.NewGuid():N}@cinema.test";
        const string password = "StrongPassword123!";

        var registerRes = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Audit Admin",
            Email = email,
            Password = password,
            ConfirmPassword = password,
            AcceptTerms = true
        });
        registerRes.EnsureSuccessStatusCode();
        var auth = (await registerRes.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>())!.Data!;
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // 5 failed login attempts from an attacker using a separate unauthenticated client
        using var attacker = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            var badLogin = await attacker.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = email,
                Password = "WrongPassword999!"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
        }

        // 6th login attempt even with correct password must be blocked by LoginCooldownUntilUtc
        var cooledDownLogin = await attacker.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, cooledDownLogin.StatusCode);

        // Active Bearer session of the real user MUST remain alive (brute-force DoS mitigated)
        var meWhileCooledDown = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meWhileCooledDown.StatusCode);

        // Admin explicit lock (LockedUntilUtc) MUST still terminate the active session immediately
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var sec = await db.UserSecurity.SingleAsync(s => s.UserId == auth.User.Id);
            Assert.NotNull(sec.LoginCooldownUntilUtc);
            Assert.Null(sec.LockedUntilUtc);
            sec.LockedUntilUtc = DateTime.UtcNow.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        var meAfterAdminLock = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterAdminLock.StatusCode);
    }

    [Fact]
    public void SignedClientIpHeader_AuthenticatesViewerIpThroughUntrustedHop_AndRejectsTamperedOrExpired()
    {
        const string secret = "super-strong-internal-proxy-secret-key-2026-hmac";
        ApiRateLimits.SetInternalProxySecret(secret);
        try
        {
            var now = DateTimeOffset.UtcNow;

            // 1. Valid signed header from untrusted NAT hop -> trusts signed viewer IP
            var validCtx = new DefaultHttpContext();
            validCtx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
            var reqHeaders = new HttpRequestMessage().Headers;
            SignedClientIpHeader.AttachSignedHeaders(reqHeaders, "203.0.113.42", secret, now);
            foreach (var h in reqHeaders)
            {
                validCtx.Request.Headers[h.Key] = h.Value.ToArray();
            }
            Assert.Equal("203.0.113.42", ApiRateLimits.GetClientIpAddress(validCtx));

            // 2. Tampered signature -> ignores spoofed IP & X-Forwarded-For, returns untrusted peer IP
            var tamperedCtx = new DefaultHttpContext();
            tamperedCtx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
            tamperedCtx.Request.Headers["X-Forwarded-For"] = "203.0.113.42";
            tamperedCtx.Request.Headers[SignedClientIpHeader.IpHeaderName] = "203.0.113.42";
            tamperedCtx.Request.Headers[SignedClientIpHeader.TimestampHeaderName] = now.ToUnixTimeSeconds().ToString();
            tamperedCtx.Request.Headers[SignedClientIpHeader.SignatureHeaderName] = new string('a', 64);
            Assert.Equal("198.51.100.77", ApiRateLimits.GetClientIpAddress(tamperedCtx));

            // 3. Expired signature (>60s) -> falls back to peer IP
            var expiredCtx = new DefaultHttpContext();
            expiredCtx.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.77");
            var expiredHeaders = new HttpRequestMessage().Headers;
            SignedClientIpHeader.AttachSignedHeaders(expiredHeaders, "203.0.113.42", secret, now.AddMinutes(-5));
            foreach (var h in expiredHeaders)
            {
                expiredCtx.Request.Headers[h.Key] = h.Value.ToArray();
            }
            Assert.Equal("198.51.100.77", ApiRateLimits.GetClientIpAddress(expiredCtx));
        }
        finally
        {
            ApiRateLimits.SetInternalProxySecret(null);
        }
    }

    [Fact]
    public async Task StreamSessionService_ConcurrentAcquireLeaseAcrossScopedInstances_EnforcesMaxLimit()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"stream-race-{Guid.NewGuid():N}")
            .Options;
        var dbFactory = new TestContextFactory(dbOptions);
        var userId = Guid.NewGuid();

        await using (var seedDb = await dbFactory.CreateDbContextAsync())
        {
            seedDb.Users.Add(new UserEntity
            {
                Id = userId,
                Email = "race@cinema.test",
                NormalizedEmail = "RACE@CINEMA.TEST",
                FullName = "Race Tester",
                Membership = MembershipTier.Free
            });
            await seedDb.SaveChangesAsync();
        }

        var hubMock = new Mock<IHubContext<CinemaHub>>();
        var clientsMock = new Mock<IHubClients>();
        var singleProxyMock = new Mock<ISingleClientProxy>();
        var groupProxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.Client(It.IsAny<string>())).Returns(singleProxyMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(groupProxyMock.Object);
        hubMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StreamSecurity:DefaultMaxStreams"] = "1",
                ["StreamSecurity:MaxConcurrentStreams:Free"] = "1"
            })
            .Build();

        // Simulate 10 concurrent Scoped DI instances racing for the same Free user
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            var scopedService = new StreamSessionService(dbFactory, hubMock.Object, TimeProvider.System, config);
            return await scopedService.AcquireLeaseAsync(
                userId,
                movieId: 1,
                episodeId: 101,
                sessionId: $"race-sess-{i}",
                deviceName: $"Device {i}");
        })).ToArray();

        await Task.WhenAll(tasks);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var activeCount = await verifyDb.ActiveStreams.CountAsync(s => s.UserId == userId && s.IsActive);
        Assert.Equal(1, activeCount);
    }

    [Theory]
    [InlineData("exploit.html", "episodes")]
    [InlineData("xss.svg", "episodes")]
    [InlineData("evil.js", "episodes")]
    [InlineData("video.mp4", "../secret")]
    [InlineData("video.mp4", "episodes/sub")]
    public async Task AdminStorageController_RejectsDangerousUploadsAndFolders(string fileName, string folder)
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"storage-sec-{Guid.NewGuid():N}")
            .Options;
        var controller = new AdminStorageController(
            new TestContextFactory(dbOptions),
            TimeProvider.System,
            Options.Create(new CloudStorageOptions { BucketName = "test-bucket" }),
            new DummyCloudStorage())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var presigned = await controller.GetPresignedUploadUrl(fileName, "text/html", folder);
        Assert.IsType<BadRequestObjectResult>(presigned.Result);

        var payload = Encoding.UTF8.GetBytes("fake-bytes");
        var formFile = new FormFile(new MemoryStream(payload), 0, payload.Length, "file", fileName);
        var uploaded = await controller.UploadToR2(formFile, folder);
        Assert.IsType<BadRequestObjectResult>(uploaded.Result);
    }

    [Fact]
    public async Task AdminStorageController_RejectsHtmlSvgDisguisedAsMp4()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"storage-polyglot-{Guid.NewGuid():N}")
            .Options;
        var controller = new AdminStorageController(
            new TestContextFactory(dbOptions),
            TimeProvider.System,
            Options.Create(new CloudStorageOptions { BucketName = "test-bucket" }),
            new DummyCloudStorage())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var svgBytes = Encoding.UTF8.GetBytes("  <svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>");
        var formFile = new FormFile(new MemoryStream(svgBytes), 0, svgBytes.Length, "file", "disguised.mp4");

        var uploaded = await controller.UploadToR2(formFile, "episodes");
        Assert.IsType<BadRequestObjectResult>(uploaded.Result);
    }

    [Theory]
    [InlineData("https://cdn.example.com/poster.webp", true, true)]
    [InlineData("/images/poster.webp", true, true)]
    [InlineData("/images/%2e%2e/secret.txt", true, false)]
    [InlineData("https://cdn.example.com/a.jpg');background:url(https://evil.test", true, false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false, false)]
    [InlineData("https://127.0.0.1/internal.png", false, false)]
    [InlineData("https://cdn.example.com:8443/poster.webp", false, false)]
    [InlineData("javascript:alert(1)", true, false)]
    public void SafeResourceUrlAttribute_BlocksEncodedTraversalCssBreakoutAndInternalHosts(
        string url,
        bool allowLocalPath,
        bool expectedValid)
    {
        var attr = new SafeResourceUrlAttribute(allowLocalPath);
        Assert.Equal(expectedValid, attr.IsValid(url));
    }

    [Theory]
    [InlineData("::127.0.0.1")]
    [InlineData("::169.254.169.254")]
    [InlineData("::10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task SafeStreamUrlValidator_BlocksIpv4CompatibleIpv6Addresses(string ipLiteral)
    {
        Assert.True(SafeStreamUrlValidator.IsInternalOrRestrictedIp(IPAddress.Parse(ipLiteral)));
        var (isSafe, _, _) = await SafeStreamUrlValidator.ValidateUrlAsync($"https://[{ipLiteral}]/video.mp4", resolveDns: false);
        Assert.False(isSafe);
    }

    [Fact]
    public void DatabaseXmlRepository_EncryptsAndDecryptsXmlWithAesGcmAndRejectsTampering()
    {
        var fallbackDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"dp-test-{Guid.NewGuid():N}"));
        var repo = new DatabaseXmlRepository(
            "Host=127.0.0.1;Port=1;Database=unused",
            "strong-master-secret-key-for-aes-256-gcm-test-2026",
            fallbackDir);

        const string sampleXml = "<key id=\"test-key\"><descriptor>secret-key-material</descriptor></key>";
        var protectedPayload = repo.ProtectXml(sampleXml);

        Assert.StartsWith("v1:aesgcm:", protectedPayload);
        Assert.DoesNotContain("secret-key-material", protectedPayload);
        Assert.Equal(sampleXml, repo.UnprotectXml(protectedPayload));

        // Tamper ciphertext byte -> must fail authentication and return null
        var tampered = protectedPayload[..^2] + (protectedPayload[^2] == 'A' ? 'B' : 'A') + protectedPayload[^1];
        Assert.Null(repo.UnprotectXml(tampered));
    }

    [Fact]
    public void PaymentWebhookVerifier_FailsClosedInStagingAndEnforcesReplayFieldsInProduction()
    {
        var stagingEnv = new Mock<IHostEnvironment>();
        stagingEnv.SetupGet(e => e.EnvironmentName).Returns("Staging");
        var emptyConfig = new ConfigurationBuilder().Build();
        var stagingVerifier = new PaymentWebhookVerifier(emptyConfig, TimeProvider.System, stagingEnv.Object);

        Assert.Throws<InvalidOperationException>(() => stagingVerifier.GetSecretKeyForGateway("VNPay"));

        var prodEnv = new Mock<IHostEnvironment>();
        prodEnv.SetupGet(e => e.EnvironmentName).Returns("Production");
        var prodConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:WebhookSecret"] = "ProdStrongWebhookSecretKey_AtLeast32Chars_99887766!"
            })
            .Build();
        var prodVerifier = new PaymentWebhookVerifier(prodConfig, TimeProvider.System, prodEnv.Object);

        var missingTs = new PaymentWebhookRequest
        {
            OrderCode = "CF-PROD-01",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TRX-123"
        };
        missingTs.Signature = prodVerifier.ComputeSignature(missingTs);
        Assert.False(prodVerifier.Verify(missingTs).IsValid);

        var missingTrx = new PaymentWebhookRequest
        {
            OrderCode = "CF-PROD-02",
            Amount = 79000m,
            Status = "PAID",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        missingTrx.Signature = prodVerifier.ComputeSignature(missingTrx);
        Assert.False(prodVerifier.Verify(missingTrx).IsValid);

        var complete = new PaymentWebhookRequest
        {
            OrderCode = "CF-PROD-03",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TRX-456",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        complete.Signature = prodVerifier.ComputeSignature(complete);
        Assert.True(prodVerifier.Verify(complete).IsValid);
    }

    [Fact]
    public async Task StreamController_PlayStream_RejectsIpMismatchEvenWithActiveLease()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"stream-ip-bind-{Guid.NewGuid():N}")
            .Options;
        var dbFactory = new TestContextFactory(dbOptions);
        var signer = new StreamUrlSigner();

        var sessions = new Mock<IStreamSessionService>();
        sessions
            .Setup(s => s.GetActiveLeaseAsync("lease-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveStreamDto
            {
                SessionId = "lease-1",
                EpisodeId = 101,
                UserId = Guid.NewGuid(),
                IpAddress = "203.0.113.10"
            });

        var controller = new StreamController(
            dbFactory,
            signer,
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            sessions.Object);

        // Sign URL for legitimate user IP 203.0.113.10
        var signed = signer.Sign("https://cinema.test", 101, 1, "203.0.113.10", sessionId: "lease-1", scope: "play");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

        // Attacker replays URL from a different IP 198.51.100.20
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.20");
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await controller.PlayStream(
            episodeId: 101,
            pos: 1,
            exp: long.Parse(query["exp"]!),
            ip: query["ip"]!,
            sig: query["sig"]!,
            sid: "lease-1",
            scope: "play",
            kid: query["kid"],
            cancellationToken: CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
    }

    [Fact]
    public async Task StreamController_HeadPlayStream_RejectsNonVipUserOnVipFastServer()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"stream-head-vip-{Guid.NewGuid():N}")
            .Options;
        var freeUserId = Guid.NewGuid();
        await using (var seedDb = new AppDbContext(dbOptions))
        {
            seedDb.Users.Add(new UserEntity
            {
                Id = freeUserId,
                Email = "free-head@cinema.test",
                NormalizedEmail = "FREE-HEAD@CINEMA.TEST",
                FullName = "Free User",
                Membership = MembershipTier.Free
            });
            seedDb.StreamSources.Add(new StreamSourceEntity
            {
                EpisodeId = 777,
                Position = 1,
                ServerName = "VIP FastCDN 1",
                ServerType = StreamServerType.VipFastServer,
                Quality = "4K UHD",
                StreamUrl = "https://cdn.example.com/vip.mp4"
            });
            await seedDb.SaveChangesAsync();
        }

        var signer = new StreamUrlSigner();
        var sessions = new Mock<IStreamSessionService>();
        sessions
            .Setup(s => s.GetActiveLeaseAsync("head-sess", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveStreamDto
            {
                SessionId = "head-sess",
                EpisodeId = 777,
                UserId = freeUserId,
                IsActive = true
            });

        var controller = new StreamController(
            new TestContextFactory(dbOptions),
            signer,
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            sessions.Object);

        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Head;
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var signed = signer.Sign("http://localhost", 777, 1, "127.0.0.1", sessionId: "head-sess", scope: "play");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

        var result = await controller.PlayStream(
            777,
            1,
            long.Parse(query["exp"]!),
            query["ip"],
            query["sig"],
            query["sid"],
            query["scope"],
            query["kid"],
            CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objResult.StatusCode);
    }
}
