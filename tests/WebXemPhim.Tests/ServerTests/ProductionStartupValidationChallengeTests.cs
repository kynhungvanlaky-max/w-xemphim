using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Security;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ProductionStartupValidationChallengeTests
{
    private const string ValidStreamKey = "Prod_StreamSigningKey_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidWebhookSecret = "Prod_PaymentWebhook_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidPayOsSecret = "Prod_PayOS_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidVnPaySecret = "Prod_VNPay_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidConnectionString = "Server=prod.db;Database=cinemaflow;User=app;Password=SuperSecret123!;";

    private static (IConfiguration Config, Mock<IWebHostEnvironment> EnvMock) CreateProductionConfig(
        Dictionary<string, string?>? overrides = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cinemaflow-challenge-{Guid.NewGuid():N}");
        var dict = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "cinema.example.test",
            ["Security:DataProtectionKeyPath"] = Path.Combine(root, "keys"),
            ["ConnectionStrings:Default"] = ValidConnectionString,
            ["Security:StreamSigningKey"] = ValidStreamKey,
            ["Payment:WebhookSecret"] = ValidWebhookSecret,
            ["Payment:Gateways:PayOS:WebhookSecret"] = ValidPayOsSecret,
            ["Payment:Gateways:VNPay:HashSecret"] = ValidVnPaySecret
        };

        if (overrides != null)
        {
            foreach (var kvp in overrides)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        envMock.SetupGet(e => e.ContentRootPath).Returns(root);

        return (config, envMock);
    }

    // =========================================================================
    // 1. Edge Case: Empty string vs missing key vs whitespace
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t\n")]
    public void Challenge_EmptyMissingWhitespace_StreamKey_ThrowsInvalidOperationException(string? badKey)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = badKey
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Security:StreamSigningKey", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t\n")]
    public void Challenge_EmptyMissingWhitespace_PaymentWebhookSecret_ThrowsInvalidOperationException(string? badSecret)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = badSecret
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Payment:WebhookSecret", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t\n")]
    public void Challenge_EmptyMissingWhitespace_ConnectionString_ThrowsInvalidOperationException(string? badConnStr)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = badConnStr
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    // =========================================================================
    // 2. Edge Case: Exactly 31 characters vs 32 characters & Padding Edge Cases
    // =========================================================================

    [Fact]
    public void Challenge_Exactly31Chars_StreamKey_ThrowsInvalidOperationException()
    {
        var key31 = new string('A', 31);
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = key31
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void Challenge_Exactly32Chars_StreamKey_Succeeds()
    {
        var key32 = new string('A', 32);
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = key32
        });

        var exception = Record.Exception(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Null(exception);
    }

    [Fact]
    public void Challenge_WhitespacePaddedShortKey_ThrowsInvalidOperationException()
    {
        // 5 actual characters + 27 trailing spaces = 32 characters total.
        // Trimmed length is 5 (< 32), so it must be rejected in Production.
        var weakPaddedKey = "short" + new string(' ', 27);
        Assert.Equal(32, weakPaddedKey.Length);
        Assert.Equal(5, weakPaddedKey.Trim().Length);

        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = weakPaddedKey
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void Challenge_WhitespacePaddedShortKey_StreamUrlSigner_ThrowsInvalidOperationException()
    {
        var weakPaddedKey = "short" + new string(' ', 27);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = weakPaddedKey
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StreamUrlSigner(config, environment: envMock.Object));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void Challenge_WhitespacePaddedShortKey_PaymentWebhookVerifier_ThrowsInvalidOperationException()
    {
        var weakPaddedKey = "short" + new string(' ', 27);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = weakPaddedKey
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway(null));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    // =========================================================================
    // 3. Edge Case: Default secret with trailing / leading spaces
    // =========================================================================

    [Fact]
    public void Challenge_DefaultSecretWithTrailingSpace_ThrowsInvalidOperationException()
    {
        var defaultWithTrailingSpace = StreamUrlSigner.DefaultSigningKey + " ";
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = defaultWithTrailingSpace
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Fact]
    public void Challenge_DefaultSecretWithTrailingSpace_StreamUrlSigner_ThrowsInvalidOperationException()
    {
        var defaultWithTrailingSpace = StreamUrlSigner.DefaultSigningKey + " ";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = defaultWithTrailingSpace
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StreamUrlSigner(config, environment: envMock.Object));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Fact]
    public void Challenge_DefaultSecretWithTrailingSpace_PaymentWebhookVerifier_ThrowsInvalidOperationException()
    {
        var defaultWebhookWithTrailingSpace = PaymentWebhookVerifier.DefaultWebhookSecret + " ";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = defaultWebhookWithTrailingSpace
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway(null));
        Assert.Contains("default placeholder key", ex.Message);
    }

    // =========================================================================
    // 4. Edge Case: Environment Variable Injection
    // =========================================================================

    [Fact]
    public void Challenge_HierarchicalEnvVar_SecurityStreamSigningKey_IsRecognized()
    {
        // ASP.NET Core translates "Security__StreamSigningKey" into "Security:StreamSigningKey"
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:StreamSigningKey"] = ValidStreamKey // Simulates ASP.NET Core env var provider mapping
            })
            .Build();

        var (fullConfig, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = config["Security:StreamSigningKey"]
        });

        var exception = Record.Exception(() =>
            WebSecurityExtensions.ValidateProduction(fullConfig, envMock.Object, client: false));
        Assert.Null(exception);
    }

    [Fact]
    public void Challenge_FlatEnvVar_StreamSigningKey_IsRecognizedWhenAppsettingsEmpty()
    {
        // In appsettings.json:
        // "Security": { "StreamSigningKey": "" }
        // When flat env var STREAM_SIGNING_KEY is provided:
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "cinema.example.test",
                ["Security:DataProtectionKeyPath"] = Path.Combine(Path.GetTempPath(), "keys"),
                ["ConnectionStrings:Default"] = ValidConnectionString,
                ["Payment:WebhookSecret"] = ValidWebhookSecret,

                // From appsettings.json:
                ["Security:StreamSigningKey"] = "",
                // From environment:
                ["STREAM_SIGNING_KEY"] = ValidStreamKey
            })
            .Build();

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        envMock.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var exception = Record.Exception(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Null(exception);
    }

    [Fact]
    public void Challenge_FlatEnvVar_ConnectionString_IsRecognizedWhenAppsettingsEmpty()
    {
        // In appsettings.json:
        // "ConnectionStrings": { "Default": "" }
        // When flat env var DATABASE_CONNECTION_STRING is provided:
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "cinema.example.test",
                ["Security:DataProtectionKeyPath"] = Path.Combine(Path.GetTempPath(), "keys"),
                ["Security:StreamSigningKey"] = ValidStreamKey,
                ["Payment:WebhookSecret"] = ValidWebhookSecret,

                // From appsettings.json:
                ["ConnectionStrings:Default"] = "",
                // From environment:
                ["DATABASE_CONNECTION_STRING"] = ValidConnectionString
            })
            .Build();

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        envMock.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var exception = Record.Exception(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Null(exception);
    }

    [Fact]
    public void Challenge_FlatEnvVar_PaymentWebhookSecret_IsRecognizedWhenAppsettingsEmpty()
    {
        // In appsettings.json:
        // "Payment": { "WebhookSecret": "" }
        // When flat env var PAYMENT_WEBHOOK_SECRET is provided:
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "cinema.example.test",
                ["Security:DataProtectionKeyPath"] = Path.Combine(Path.GetTempPath(), "keys"),
                ["ConnectionStrings:Default"] = ValidConnectionString,
                ["Security:StreamSigningKey"] = ValidStreamKey,

                // From appsettings.json:
                ["Payment:WebhookSecret"] = "",
                // From environment:
                ["PAYMENT_WEBHOOK_SECRET"] = ValidWebhookSecret
            })
            .Build();

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        envMock.SetupGet(e => e.ContentRootPath).Returns(Path.GetTempPath());

        var exception = Record.Exception(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Null(exception);
    }

    [Fact]
    public void Challenge_FlatEnvVar_StreamUrlSigner_IsRecognizedWhenAppsettingsEmpty()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:StreamSigningKey"] = "",
                ["STREAM_SIGNING_KEY"] = ValidStreamKey
            })
            .Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var signer = new StreamUrlSigner(config, environment: envMock.Object);
        var signed = signer.Sign("https://cinemaflow.vn", 1, 0, "127.0.0.1");

        Assert.NotNull(signed.StreamUrl);
        Assert.Contains("sig=", signed.StreamUrl);
    }

    [Fact]
    public void Challenge_FlatEnvVar_PaymentWebhookVerifier_IsRecognizedWhenAppsettingsEmpty()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:WebhookSecret"] = "",
                ["PAYMENT_WEBHOOK_SECRET"] = ValidWebhookSecret,
                ["Payment:Gateways:PayOS:WebhookSecret"] = "",
                ["PAYOS_WEBHOOK_SECRET"] = ValidPayOsSecret,
                ["Payment:Gateways:VNPay:HashSecret"] = "",
                ["VNPAY_HASH_SECRET"] = ValidVnPaySecret
            })
            .Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);

        Assert.Equal(ValidWebhookSecret, verifier.GetSecretKeyForGateway(null));
        Assert.Equal(ValidPayOsSecret, verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Equal(ValidVnPaySecret, verifier.GetSecretKeyForGateway("VNPay"));
    }
}
