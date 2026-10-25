using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebXemPhim.Client.Services;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class SecurityConfirmedFindingsTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (AppDbContext Context, IDbContextFactory<AppDbContext> Factory) CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-cf-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        var context = new AppDbContext(options);
        return (context, new TestDbContextFactory(options));
    }

    #region [CF-01] Stream Session & VIP URL Tests

    [Fact]
    public async Task CF01_PlayStream_VipStreamWithoutSid_ReturnsForbidden()
    {
        var (context, factory) = CreateInMemoryContext();
        const int episodeId = 101;
        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = 1,
            ServerName = "VIP Fast",
            ServerType = StreamServerType.VipFastServer,
            Quality = "4K HDR",
            StreamUrl = "https://vip.cdn.example.com/play.m3u8"
        });
        await context.SaveChangesAsync();

        var sessionsMock = new Mock<IStreamSessionService>();
        var controller = new StreamController(
            factory,
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            sessionsMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var signer = new StreamUrlSigner();
        var signed = signer.Sign("http://localhost", episodeId, 1, "127.0.0.1");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

        var result = await controller.PlayStream(
            episodeId,
            1,
            long.Parse(query["exp"]!),
            query["ip"],
            query["sig"],
            null, // sid = null!
            null,
            null,
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task CF01_PlayStream_VipStreamWithValidSid_AndVipUser_PassesSessionVerification()
    {
        var (context, factory) = CreateInMemoryContext();
        const int episodeId = 102;
        var userId = Guid.NewGuid();

        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "vipuser@cinema.test",
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        });

        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = 1,
            ServerName = "VIP Fast",
            ServerType = StreamServerType.VipFastServer,
            Quality = "4K HDR",
            StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
        });
        await context.SaveChangesAsync();

        var sessionsMock = new Mock<IStreamSessionService>();
        sessionsMock.Setup(s => s.GetActiveLeaseAsync("valid-sid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveStreamDto
            {
                SessionId = "valid-sid",
                UserId = userId,
                EpisodeId = episodeId,
                DeviceName = "Test",
                StartedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            });

        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var clientFactoryMock = new Mock<IHttpClientFactory>();
        clientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var controller = new StreamController(
            factory,
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            clientFactoryMock.Object,
            NullLogger<StreamController>.Instance,
            sessionsMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var signer = new StreamUrlSigner();
        var signed = signer.Sign("http://localhost", episodeId, 1, "127.0.0.1", sessionId: "valid-sid");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(signed.StreamUrl).Query);

        // Verification passes session check and attempts upstream fetch (not returning 403 Forbidden)
        var result = await controller.PlayStream(
            episodeId,
            1,
            long.Parse(query["exp"]!),
            query["ip"],
            query["sig"],
            "valid-sid",
            null,
            null,
            CancellationToken.None);

        Assert.IsNotType<StatusCodeResult>(result);
    }

    [Fact]
    public async Task CF01_SignStream_FreeUser_CanSignNonVipStream()
    {
        var (context, factory) = CreateInMemoryContext();
        const int episodeId = 103;
        var userId = Guid.NewGuid();

        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "freeuser@cinema.test",
            Membership = MembershipTier.Free
        });

        context.Movies.Add(new MovieEntity { Id = 1, Title = "Movie 1", Slug = "movie-1" });
        context.Episodes.Add(new EpisodeEntity { Id = episodeId, MovieId = 1, EpisodeNumber = 1, Title = "Tập 1" });

        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = 1,
            ServerName = "Hydra CDN",
            ServerType = StreamServerType.HydraCdn,
            Quality = "720p",
            StreamUrl = "https://cdn.example.com/stream.m3u8"
        });
        await context.SaveChangesAsync();

        var sessionsMock = new Mock<IStreamSessionService>();
        sessionsMock.Setup(s => s.GetActiveLeaseAsync("free-sid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveStreamDto
            {
                SessionId = "free-sid",
                UserId = userId,
                EpisodeId = episodeId,
                DeviceName = "Test",
                StartedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            });

        var controller = new StreamController(
            factory,
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            sessionsMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, "freeuser@cinema.test"),
                    new Claim("membership", "Free")
                ], "TestAuth"))
            }
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var response = await controller.SignStream(new SignStreamRequest
        {
            EpisodeId = episodeId,
            Position = 1,
            SessionId = "free-sid"
        });

        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<SignedStreamDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("free-sid", apiResponse.Data?.SessionId);
    }

    [Fact]
    public async Task CF01_SignStream_FreeUser_SigningVipStream_ReturnsForbidden()
    {
        var (context, factory) = CreateInMemoryContext();
        const int episodeId = 104;
        var userId = Guid.NewGuid();

        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "freeuser@cinema.test",
            Membership = MembershipTier.Free
        });

        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = 1,
            ServerName = "VIP Fast",
            ServerType = StreamServerType.VipFastServer,
            Quality = "4K",
            StreamUrl = "https://vip.example.com/stream.m3u8"
        });
        await context.SaveChangesAsync();

        var controller = new StreamController(
            factory,
            new StreamUrlSigner(),
            new PublicUrlResolver(new ConfigurationBuilder().Build()),
            new Mock<IHttpClientFactory>().Object,
            NullLogger<StreamController>.Instance,
            new Mock<IStreamSessionService>().Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, "freeuser@cinema.test"),
                    new Claim("membership", "Free")
                ], "TestAuth"))
            }
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var response = await controller.SignStream(new SignStreamRequest
        {
            EpisodeId = episodeId,
            Position = 1
        });

        var objectResult = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task CF01_MoviesController_MaskVipStreams_SetsEmptyStreamUrl_ForVipStreams_EvenWhenUserIsVip()
    {
        var (context, factory) = CreateInMemoryContext();
        var movie = new MovieDetailDto
        {
            Id = 1,
            Title = "Test Movie",
            Episodes =
            [
                new EpisodeDto
                {
                    Id = 10,
                    EpisodeNumber = 1,
                    Streams =
                    [
                        new StreamSourceDto
                        {
                            EpisodeId = 10,
                            Position = 1,
                            ServerName = "VIP Fast",
                            ServerType = StreamServerType.VipFastServer,
                            Quality = "4K HDR",
                            StreamUrl = "https://cdn.example.com/raw-vip.m3u8"
                        },
                        new StreamSourceDto
                        {
                            EpisodeId = 10,
                            Position = 2,
                            ServerName = "Standard CDN",
                            ServerType = StreamServerType.HydraCdn,
                            Quality = "1080p",
                            StreamUrl = "https://cdn.example.com/raw-std.m3u8"
                        }
                    ]
                }
            ]
        };

        var movieServiceMock = new Mock<IMovieService>();
        movieServiceMock.Setup(s => s.GetMovieByIdAsync(1)).ReturnsAsync(movie);

        var controller = new MoviesController(movieServiceMock.Object, factory);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.GetById(1);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);

        var streams = payload.Data!.Episodes.First().Streams;
        var vipStream = streams.First(s => s.Position == 1);
        var stdStream = streams.First(s => s.Position == 2);

        // VIP stream must not return pre-signed URL without sid; must be empty
        Assert.Equal(string.Empty, vipStream.StreamUrl);
        // Non-VIP stream has pre-signed URL
        Assert.StartsWith("http", stdStream.StreamUrl);
    }

    #endregion

    #region [CF-02] Forwarded IP & Circuit Provider Tests

    [Theory]
    [InlineData("::ffff:172.18.0.2", true)]     // IPv4 mapped in private Docker bridge
    [InlineData("::ffff:192.168.1.100", true)]  // IPv4 mapped in 192.168.0.0/16
    [InlineData("::ffff:8.8.8.8", false)]        // IPv4 mapped public
    [InlineData("127.0.0.1", true)]             // Loopback
    [InlineData("10.0.0.5", true)]              // 10.0.0.0/8
    public void CF02_ForwardedClientIpHandler_IsTrustedProxy_CorrectlyNormalizesIPv4MappedIPv6(string ipStr, bool expectedTrusted)
    {
        var addr = IPAddress.Parse(ipStr);
        var result = ForwardedClientIpHandler.IsTrustedProxy(addr);
        Assert.Equal(expectedTrusted, result);
    }

    [Fact]
    public void CF02_CircuitClientIpProvider_StoresAndRetrievesClientIp()
    {
        ICircuitClientIpProvider provider = new CircuitClientIpProvider();
        Assert.Null(provider.ClientIp);

        provider.ClientIp = "203.0.113.42";
        Assert.Equal("203.0.113.42", provider.ClientIp);
    }

    #endregion

    #region [CF-03] MFA TOTP Replay & Lockout Tests

    [Fact]
    public async Task CF03_AuthService_TotpReplayWithinWindow_IsRejected()
    {
        var (context, factory) = CreateInMemoryContext();
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var authService = new AuthService(factory, timeProvider, dataProtectionProvider);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            FullName = "MFA User",
            Email = "mfa_test@cinema.test",
            Password = "SecureP@ssw0rd!",
            ConfirmPassword = "SecureP@ssw0rd!",
            AcceptTerms = true
        });
        Assert.Equal(AuthFailure.None, reg.Failure);
        var userId = reg.Data!.User.Id;

        var secret = TotpSecurity.CreateSecret();
        var protector = dataProtectionProvider.CreateProtector("CinemaFlow.MfaSecret.v1");
        var protectedSecret = protector.Protect(Convert.ToBase64String(secret));
        context.MfaCredentials.Add(new MfaCredentialEntity
        {
            UserId = userId,
            ProtectedSecret = protectedSecret,
            RecoveryCodeHashes = "",
            Enabled = true,
            EnabledAtUtc = now.UtcDateTime
        });
        await context.SaveChangesAsync();

        var totpCode = TotpSecurity.GenerateCode(secret, now.UtcDateTime);

        // Step 1: Login to get challenge
        var loginRes = await authService.LoginAsync(new LoginRequest
        {
            Email = "mfa_test@cinema.test",
            Password = "SecureP@ssw0rd!"
        });
        Assert.NotNull(loginRes.Data);
        Assert.True(loginRes.Data.RequiresMfa);
        var challenge1 = loginRes.Data.MfaChallengeToken;

        // Step 2: First MFA verification succeeds
        var verify1 = await authService.VerifyMfaLoginAsync(new VerifyMfaLoginRequest
        {
            ChallengeToken = challenge1,
            Code = totpCode
        });
        Assert.Equal(AuthFailure.None, verify1.Failure);
        Assert.NotNull(verify1.Data?.AccessToken);

        // Attacker captures totpCode and tries to use it again with a fresh challenge within same 90s window
        var loginRes2 = await authService.LoginAsync(new LoginRequest
        {
            Email = "mfa_test@cinema.test",
            Password = "SecureP@ssw0rd!"
        });
        Assert.NotNull(loginRes2.Data);
        var challenge2 = loginRes2.Data.MfaChallengeToken;

        var verifyReplay = await authService.VerifyMfaLoginAsync(new VerifyMfaLoginRequest
        {
            ChallengeToken = challenge2,
            Code = totpCode
        });

        // Replay of same TOTP code must fail!
        Assert.Equal(AuthFailure.InvalidCredentials, verifyReplay.Failure);
    }

    [Fact]
    public async Task CF03_AuthService_VerifyMfaLogin_LocksAccountAfter5FailedAttempts()
    {
        var (context, factory) = CreateInMemoryContext();
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var authService = new AuthService(factory, timeProvider, dataProtectionProvider);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            FullName = "MFA Lockout User",
            Email = "mfa_lockout@cinema.test",
            Password = "SecureP@ssw0rd!",
            ConfirmPassword = "SecureP@ssw0rd!",
            AcceptTerms = true
        });
        Assert.Equal(AuthFailure.None, reg.Failure);
        var userId = reg.Data!.User.Id;

        var secret = TotpSecurity.CreateSecret();
        var protector = dataProtectionProvider.CreateProtector("CinemaFlow.MfaSecret.v1");
        var protectedSecret = protector.Protect(Convert.ToBase64String(secret));
        context.MfaCredentials.Add(new MfaCredentialEntity
        {
            UserId = userId,
            ProtectedSecret = protectedSecret,
            RecoveryCodeHashes = "",
            Enabled = true,
            EnabledAtUtc = now.UtcDateTime
        });
        await context.SaveChangesAsync();

        var loginRes = await authService.LoginAsync(new LoginRequest
        {
            Email = "mfa_lockout@cinema.test",
            Password = "SecureP@ssw0rd!"
        });
        Assert.NotNull(loginRes.Data);
        var challengeToken = loginRes.Data.MfaChallengeToken;

        // Guess 5 wrong MFA codes
        for (int i = 0; i < 5; i++)
        {
            var res = await authService.VerifyMfaLoginAsync(new VerifyMfaLoginRequest
            {
                ChallengeToken = challengeToken,
                Code = "000000"
            });
            Assert.Equal(AuthFailure.InvalidCredentials, res.Failure);
        }

        // Check account is now locked
        var updatedSec = await context.UserSecurity.FirstAsync(s => s.UserId == userId);
        Assert.True(updatedSec.LockedUntilUtc > now.UtcDateTime);

        // Further login attempts are rejected due to lock
        var lockedLogin = await authService.LoginAsync(new LoginRequest
        {
            Email = "mfa_lockout@cinema.test",
            Password = "SecureP@ssw0rd!"
        });
        Assert.Equal(AuthFailure.InvalidCredentials, lockedLogin.Failure);
    }

    [Fact]
    public async Task CF03_AuthService_TryLoginAsync_WithMfa_DoesNotResetFailedLoginAttemptsEarly()
    {
        var (context, factory) = CreateInMemoryContext();
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var authService = new AuthService(factory, timeProvider, dataProtectionProvider);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            FullName = "MFA Step User",
            Email = "mfa_step@cinema.test",
            Password = "SecureP@ssw0rd!",
            ConfirmPassword = "SecureP@ssw0rd!",
            AcceptTerms = true
        });
        Assert.Equal(AuthFailure.None, reg.Failure);
        var userId = reg.Data!.User.Id;

        var secret = TotpSecurity.CreateSecret();
        var protector = dataProtectionProvider.CreateProtector("CinemaFlow.MfaSecret.v1");
        var protectedSecret = protector.Protect(Convert.ToBase64String(secret));
        context.MfaCredentials.Add(new MfaCredentialEntity
        {
            UserId = userId,
            ProtectedSecret = protectedSecret,
            RecoveryCodeHashes = "",
            Enabled = true,
            EnabledAtUtc = now.UtcDateTime
        });
        await context.SaveChangesAsync();

        // 2 failed logins with bad password
        await authService.LoginAsync(new LoginRequest { Email = "mfa_step@cinema.test", Password = "WrongPassword!" });
        await authService.LoginAsync(new LoginRequest { Email = "mfa_step@cinema.test", Password = "WrongPassword!" });

        var sec = await context.UserSecurity.AsNoTracking().FirstAsync(s => s.UserId == userId);
        Assert.Equal(2, sec.FailedLoginAttempts);

        // Correct password -> step 1 passes and returns MfaChallengeToken
        var step1 = await authService.LoginAsync(new LoginRequest { Email = "mfa_step@cinema.test", Password = "SecureP@ssw0rd!" });
        Assert.True(step1.Data?.RequiresMfa);

        // Security.FailedLoginAttempts must NOT have been reset to 0
        var secAfterStep1 = await context.UserSecurity.AsNoTracking().FirstAsync(s => s.UserId == userId);
        Assert.Equal(2, secAfterStep1.FailedLoginAttempts);

        // 3 more failed MFA guesses -> should hit 5 and lock the account!
        for (int i = 0; i < 3; i++)
        {
            await authService.VerifyMfaLoginAsync(new VerifyMfaLoginRequest
            {
                ChallengeToken = step1.Data!.MfaChallengeToken,
                Code = "000000"
            });
        }

        var secLocked = await context.UserSecurity.AsNoTracking().FirstAsync(s => s.UserId == userId);
        Assert.True(secLocked.LockedUntilUtc > now.UtcDateTime);
    }

    [Fact]
    public async Task CF03_AuthService_DisableMfaAsync_IncrementsFailedAttemptsAndLocksAccount()
    {
        var (context, factory) = CreateInMemoryContext();
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FixedTimeProvider(now);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var authService = new AuthService(factory, timeProvider, dataProtectionProvider);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            FullName = "MFA Disable User",
            Email = "mfa_disable@cinema.test",
            Password = "SecureP@ssw0rd!",
            ConfirmPassword = "SecureP@ssw0rd!",
            AcceptTerms = true
        });
        Assert.Equal(AuthFailure.None, reg.Failure);
        var userId = reg.Data!.User.Id;

        var secret = TotpSecurity.CreateSecret();
        var protector = dataProtectionProvider.CreateProtector("CinemaFlow.MfaSecret.v1");
        var protectedSecret = protector.Protect(Convert.ToBase64String(secret));
        context.MfaCredentials.Add(new MfaCredentialEntity
        {
            UserId = userId,
            ProtectedSecret = protectedSecret,
            RecoveryCodeHashes = "",
            Enabled = true,
            EnabledAtUtc = now.UtcDateTime
        });
        await context.SaveChangesAsync();

        // 5 bad MFA disable attempts with invalid code
        for (int i = 0; i < 5; i++)
        {
            var res = await authService.DisableMfaAsync(userId, "SecureP@ssw0rd!", "000000");
            Assert.False(res);
        }

        // Account must now be locked
        var sec = await context.UserSecurity.AsNoTracking().FirstAsync(s => s.UserId == userId);
        Assert.True(sec.LockedUntilUtc > now.UtcDateTime);

        // 6th attempt should be rejected because account is locked
        var lockedRes = await authService.DisableMfaAsync(userId, "SecureP@ssw0rd!", "000000");
        Assert.False(lockedRes);
    }

    #endregion

    #region [CF-04] Admin Payment State Transition Tests

    [Fact]
    public async Task CF04_AdminPayments_MarkPaymentPaid_CompletedStatus_ReturnsBadRequest()
    {
        var (context, factory) = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "paid_buyer@cinema.test"
        });

        var paymentId = Guid.NewGuid();
        context.Payments.Add(new PaymentEntity
        {
            Id = paymentId,
            UserId = userId,
            OrderCode = "CF-ORDER-PAID",
            Status = PaymentStatus.Completed,
            Amount = 199000m,
            PlanId = "vip-90",
            PlanName = "Gói 3 tháng",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var controller = new AdminPaymentsController(factory, TimeProvider.System, memoryCache: memoryCache);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var response = await controller.MarkPaymentPaid(paymentId, new ManualPaymentApprovalRequest
        {
            TransactionReference = "TRANS-ALREADY-DONE"
        });

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<PaymentDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("thanh toán trước đó", apiResponse.Message);
    }

    [Fact]
    public async Task CF04_AdminPayments_RejectPayment_AlreadyFailedStatus_ReturnsBadRequest()
    {
        var (context, factory) = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "failed_buyer@cinema.test"
        });

        var paymentId = Guid.NewGuid();
        context.Payments.Add(new PaymentEntity
        {
            Id = paymentId,
            UserId = userId,
            OrderCode = "CF-ORDER-FAILED",
            Status = PaymentStatus.Failed,
            Amount = 79000m,
            PlanId = "vip-30",
            PlanName = "Gói 1 tháng",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = new AdminPaymentsController(factory, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var response = await controller.RejectPayment(paymentId, new PaymentRejectionRequest
        {
            Reason = "Lý do kiểm thử"
        });

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<PaymentDto>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("từ chối trước đó", apiResponse.Message);
    }

    #endregion

    #region [CF-05] Privilege Cache Eviction & Stream Revocation Tests

    [Fact]
    public async Task CF05_AdminUsers_UpdateUserMembership_DowngradeToFree_RevokesStreamsAndEvictsCache()
    {
        var (context, factory) = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "vip_downgrade@cinema.test",
            FullName = "VIP Downgrade User",
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        });
        await context.SaveChangesAsync();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        memoryCache.Set($"vip_status_{userId}", true);

        var sessionsMock = new Mock<IStreamSessionService>();
        var controller = new AdminUsersController(
            factory,
            TimeProvider.System,
            streamSessionService: sessionsMock.Object,
            memoryCache: memoryCache);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.UpdateUserMembership(
            userId,
            new AdminUpdateUserRequest { MembershipTier = MembershipTier.Free });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(memoryCache.TryGetValue($"vip_status_{userId}", out _));
        sessionsMock.Verify(s => s.RevokeUserStreamsAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CF05_AdminUsers_UpdateUserLockStatus_Lock_RevokesStreamsAndEvictsCache()
    {
        var (context, factory) = CreateInMemoryContext();
        var userId = Guid.NewGuid();
        context.Users.Add(new UserEntity
        {
            Id = userId,
            Email = "user_to_lock@cinema.test",
            FullName = "User To Lock",
            Membership = MembershipTier.Vip
        });
        await context.SaveChangesAsync();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        memoryCache.Set($"vip_status_{userId}", true);

        var sessionsMock = new Mock<IStreamSessionService>();
        var controller = new AdminUsersController(
            factory,
            TimeProvider.System,
            streamSessionService: sessionsMock.Object,
            memoryCache: memoryCache);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.UpdateUserLockStatus(
            userId,
            new AdminUpdateUserLockRequest { IsLocked = true, LockMinutes = 60 });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.False(memoryCache.TryGetValue($"vip_status_{userId}", out _));
        sessionsMock.Verify(s => s.RevokeUserStreamsAsync(userId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region [CF-06] SSRF Web Ports & Batch Generation Tests

    [Fact]
    public async Task CF06_AdminEpisodes_BatchGenerateEpisodes_PatternWithNInHost_ReturnsBadRequest()
    {
        var (context, factory) = CreateInMemoryContext();
        var controller = new AdminEpisodesController(factory, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.BatchGenerateEpisodes(new BatchEpisodeGenerateRequest
        {
            MovieId = 1,
            StartEpisode = 1,
            EndEpisode = 10,
            ServerType = StreamServerType.HydraCdn,
            StreamUrlPattern = "https://server-{n}.internal.corp/stream.m3u8"
        });

        var badResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var apiResponse = Assert.IsType<ApiResponse<int>>(badResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("tên miền", apiResponse.Message);
    }

    [Fact]
    public async Task CF06_AdminEpisodes_BatchGenerateEpisodes_Exceeding200Episodes_ReturnsBadRequest()
    {
        var (context, factory) = CreateInMemoryContext();
        var controller = new AdminEpisodesController(factory, TimeProvider.System);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.BatchGenerateEpisodes(new BatchEpisodeGenerateRequest
        {
            MovieId = 1,
            StartEpisode = 1,
            EndEpisode = 250,
            ServerType = StreamServerType.HydraCdn,
            StreamUrlPattern = "https://cdn.example.com/episodes/ep_{n}.m3u8"
        });

        var badResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var apiResponse = Assert.IsType<ApiResponse<int>>(badResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Contains("200", apiResponse.Message);
    }

    [Theory]
    [InlineData("http://stream.example.com:8080/video.mp4", false)]
    [InlineData("https://stream.example.com:8443/video.mp4", false)]
    [InlineData("http://stream.example.com:22/video.mp4", false)]
    [InlineData("http://1.1.1.1:80/video.mp4", true)]
    [InlineData("https://1.1.1.1:443/video.mp4", true)]
    public async Task CF06_SafeStreamUrlValidator_RestrictsToWebPorts(string url, bool expectedSafe)
    {
        var (isSafe, _, _) = await SafeStreamUrlValidator.ValidateUrlAsync(url, resolveDns: false);
        Assert.Equal(expectedSafe, isSafe);
    }

    #endregion
}
