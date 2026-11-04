using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Security;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Regression tests for the second security audit: deployment defaults, demo-data gating,
/// bootstrap administrators, proxy IP resolution, MFA recovery codes and pagination bounds.
/// </summary>
public sealed class SecurityHardeningRegressionTests
{
    private const string ValidStreamKey = "Prod_StreamSigningKey_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidWebhookSecret = "Prod_PaymentWebhook_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidConnectionString = "Server=prod.db;Database=cinemaflow;User=app;Password=SuperSecret123!;";
    private const string StrongBootstrapPassword = "Bootstrap_Admin_2026!";

    private static (IConfiguration Config, Mock<IWebHostEnvironment> EnvMock) CreateProductionConfig(
        Dictionary<string, string?>? overrides = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cinemaflow-hardening-{Guid.NewGuid():N}");
        var dict = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "cinema.example.test",
            ["Security:DataProtectionKeyPath"] = Path.Combine(root, "keys"),
            ["ConnectionStrings:Default"] = ValidConnectionString,
            ["Security:StreamSigningKey"] = ValidStreamKey,
            ["Payment:WebhookSecret"] = ValidWebhookSecret
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                dict[key] = value;
            }
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        envMock.SetupGet(e => e.ContentRootPath).Returns(root);
        return (config, envMock);
    }

    // ------------------------------------------------------------ deployment defaults

    [Fact]
    public void ProductionValidation_RejectsWildcardAllowedHosts_EvenBehindReverseProxy()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Security:BehindReverseProxy"] = "true"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Contains("AllowedHosts", ex.Message);
    }

    [Theory]
    [InlineData("CinemaFlow_Production_Stream_Signing_HMAC_Secret_2026_Key!")]
    [InlineData("ReplaceWithStrongRandomSecretKeyOfAtLeast32BytesLength_2026!")]
    [InlineData("ChangeThisToStrongSecurePassword_2026!")]
    [InlineData("CHANGE_ME_TO_A_RANDOM_SECRET_VALUE_2026!")]
    public void ProductionValidation_RejectsPublicPlaceholderStreamKeys(string placeholder)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = placeholder
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Contains("Security:StreamSigningKey", ex.Message);
    }

    [Fact]
    public void ProductionValidation_RejectsDemoCouponSeeding()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Database:SeedDemoCoupons"] = "true"
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Contains("Demo", ex.Message);
    }

    [Fact]
    public void ProductionValidation_RequiresBootstrapEmailAndPasswordTogether()
    {
        var (emailOnly, envMock1) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Database:BootstrapAdminEmail"] = "admin@example.test"
        });
        Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(emailOnly, envMock1.Object, client: false));

        var (passwordOnly, envMock2) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Database:BootstrapAdminPassword"] = StrongBootstrapPassword
        });
        Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(passwordOnly, envMock2.Object, client: false));
    }

    [Theory]
    [InlineData("Password123!")]
    [InlineData("short")]
    [InlineData("admin123")]
    public void ProductionValidation_RejectsWeakBootstrapPassword(string password)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Database:BootstrapAdminEmail"] = "admin@example.test",
            ["Database:BootstrapAdminPassword"] = password
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Contains("BootstrapAdminPassword", ex.Message);
    }

    [Fact]
    public void ProductionValidation_AcceptsStrongBootstrapCredentials()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Database:BootstrapAdminEmail"] = "admin@example.test",
            ["Database:BootstrapAdminPassword"] = StrongBootstrapPassword
        });

        WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false);
    }

    // ------------------------------------------------------------ bootstrap administrator

    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-hardening-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SeedEssentialAsync_WithoutBootstrap_DoesNotCreateWellKnownAdminOrCoupons()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedEssentialAsync(context, TimeProvider.System);

        Assert.Empty(await context.Users.ToListAsync());
        Assert.Empty(await context.Coupons.ToListAsync());
    }

    [Fact]
    public async Task SeedEssentialAsync_WithBootstrap_CreatesOnlyTheConfiguredAdmin()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedEssentialAsync(
            context,
            TimeProvider.System,
            bootstrapAdminEmail: "bootstrap@example.test",
            bootstrapAdminPassword: StrongBootstrapPassword);

        var user = Assert.Single(await context.Users.ToListAsync());
        Assert.Equal("bootstrap@example.test", user.Email);
        Assert.Empty(await context.Coupons.ToListAsync());

        var security = Assert.Single(await context.UserSecurity.ToListAsync());
        Assert.True(security.IsAdmin);
        Assert.True(CredentialSecurity.Verify(
            StrongBootstrapPassword,
            user.PasswordSalt,
            user.PasswordHash,
            security.PasswordIterations));
    }

    [Fact]
    public async Task SeedEssentialAsync_WithDefaultCouponOptIn_CreatesOnlyPublicCatalogCoupons()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedEssentialAsync(
            context,
            TimeProvider.System,
            seedDefaultCoupons: true);

        var coupons = await context.Coupons.ToListAsync();
        Assert.Equal(2, coupons.Count);
        Assert.Contains(coupons, coupon => coupon.Code == "CINEMA2026");
        Assert.Contains(coupons, coupon => coupon.Code == "VIPPRO");
        Assert.DoesNotContain(coupons, coupon => coupon.Code == "TESTVIP");
        Assert.Empty(await context.Users.ToListAsync());
    }

    // ------------------------------------------------------------ demo coupon gating

    private static PaymentService CreatePaymentService(string environmentName, bool seedDemoCoupons = false)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-coupon-gating-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns(environmentName);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:SeedDemoCoupons"] = seedDemoCoupons.ToString()
        }).Build();
        return new PaymentService(factory, TimeProvider.System, environment.Object, config);
    }

    [Fact]
    public async Task PaymentService_Production_DoesNotSeedDemoCoupons()
    {
        var service = CreatePaymentService("Production");
        var result = await service.ValidateCouponAsync(new WebXemPhim.Shared.Requests.ApplyCouponRequest
        {
            PlanId = "vip-30",
            CouponCode = "TESTVIP"
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task PaymentService_Testing_SeedsDemoCouponsForTestFixtures()
    {
        var service = CreatePaymentService("Testing");
        var result = await service.ValidateCouponAsync(new WebXemPhim.Shared.Requests.ApplyCouponRequest
        {
            PlanId = "vip-30",
            CouponCode = "TESTVIP"
        });

        Assert.True(result.IsValid);
        Assert.Equal(50m, result.DiscountPercent);
    }

    // ------------------------------------------------------------ proxy IP resolution

    [Fact]
    public void ApiRateLimits_UsesRightmostUntrustedForwardedForEntry()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.1, 203.0.113.5";

        Assert.Equal("203.0.113.5", ApiRateLimits.GetClientIpAddress(context));
    }

    [Fact]
    public void ApiRateLimits_SkipsConfiguredTrustedProxyAtTheRightEdge()
    {
        const string trustedProxy = "10.251.0.1";
        ApiRateLimits.AddTrustedProxy(trustedProxy);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = $"198.51.100.1, {trustedProxy}";

        Assert.Equal("198.51.100.1", ApiRateLimits.GetClientIpAddress(context));
    }

    // ------------------------------------------------------------ MFA recovery codes

    [Fact]
    public void RecoveryCodes_AreLongEnoughAndUseSaltedHashes()
    {
        var code = CredentialSecurity.CreateRecoveryCode();
        Assert.Equal(20, code.Length);

        var firstHash = CredentialSecurity.HashRecoveryCode(code);
        var secondHash = CredentialSecurity.HashRecoveryCode(code);

        Assert.StartsWith("v2:", firstHash);
        Assert.NotEqual(firstHash, secondHash);
        Assert.True(CredentialSecurity.VerifyRecoveryCode(code, firstHash));
        Assert.False(CredentialSecurity.VerifyRecoveryCode("00000000000000000000", firstHash));
    }

    [Fact]
    public void LegacyUnsaltedRecoveryCodeHash_RemainsVerifiable()
    {
        var code = CredentialSecurity.CreateRecoveryCode();
        var legacyHash = CredentialSecurity.TokenHash(code);

        Assert.True(CredentialSecurity.VerifyRecoveryCode(code, legacyHash));
        Assert.False(CredentialSecurity.VerifyRecoveryCode("wrong-code", legacyHash));
    }

    // ------------------------------------------------------------ pagination bounds

    [Fact]
    public void Pagination_ClampsHugeValuesWithoutIntegerOverflow()
    {
        var (page, pageSize, skip) = Pagination.Normalize(int.MaxValue, int.MaxValue);

        Assert.Equal(Pagination.MaxPage, page);
        Assert.Equal(Pagination.MaxPageSize, pageSize);
        Assert.True(skip >= 0);
        Assert.Equal((long)(Pagination.MaxPage - 1) * Pagination.MaxPageSize, skip);
    }

    [Fact]
    public void Pagination_UsesDefaultsForNonPositiveValues()
    {
        var (page, pageSize, skip) = Pagination.Normalize(-5, -1, defaultPageSize: 50, maxPageSize: 100);

        Assert.Equal(1, page);
        Assert.Equal(50, pageSize);
        Assert.Equal(0, skip);
    }

    [Fact]
    public void EpisodeThumbnailUpload_HasAnExplicitRequestSizeLimit()
    {
        var method = typeof(AdminEpisodesController).GetMethod(nameof(AdminEpisodesController.UploadEpisodeThumbnail));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true).SingleOrDefault());
    }
}
