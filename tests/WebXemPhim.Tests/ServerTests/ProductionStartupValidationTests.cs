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

public sealed class ProductionStartupValidationTests
{
    private const string ValidStreamKey = "Prod_StreamSigningKey_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidWebhookSecret = "Prod_PaymentWebhook_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidPayOsSecret = "Prod_PayOS_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidVnPaySecret = "Prod_VNPay_CustomSecure_2026_AtLeast32Bytes!";
    private const string ValidConnectionString = "Server=prod.db;Database=cinemaflow;User=app;Password=SuperSecret123!;";

    private static (IConfiguration Config, Mock<IWebHostEnvironment> EnvMock) CreateProductionConfig(
        Dictionary<string, string?>? overrides = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cinemaflow-prod-test-{Guid.NewGuid():N}");
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

    [Fact]
    public void ValidateProduction_ValidConfiguration_SucceedsWithoutException()
    {
        var (config, envMock) = CreateProductionConfig();
        // Should not throw
        WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateProduction_MissingOrEmptyStreamSigningKey_ThrowsInvalidOperationException(string? key)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = key
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Security:StreamSigningKey", ex.Message);
    }

    [Fact]
    public void ValidateProduction_DefaultStreamSigningKey_ThrowsInvalidOperationException()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StreamUrlSigner.DefaultSigningKey
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Security:StreamSigningKey cannot be the default placeholder key", ex.Message);
    }

    [Theory]
    [InlineData("too_short_key")]
    [InlineData("1234567890123456789012345678901")] // 31 chars
    public void ValidateProduction_ShortStreamSigningKey_ThrowsInvalidOperationException(string shortKey)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = shortKey
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Security:StreamSigningKey must be at least 32 characters long", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateProduction_MissingOrEmptyPaymentWebhookSecret_ThrowsInvalidOperationException(string? secret)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = secret
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Payment:WebhookSecret", ex.Message);
    }

    [Fact]
    public void ValidateProduction_DefaultPaymentWebhookSecret_ThrowsInvalidOperationException()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = PaymentWebhookVerifier.DefaultWebhookSecret
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Payment:WebhookSecret cannot be the default placeholder key", ex.Message);
    }

    [Theory]
    [InlineData("short_webhook_sec")]
    [InlineData("1234567890123456789012345678901")] // 31 chars
    public void ValidateProduction_ShortPaymentWebhookSecret_ThrowsInvalidOperationException(string shortSecret)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = shortSecret
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("Payment:WebhookSecret must be at least 32 characters long", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateProduction_MissingOrEmptyConnectionString_ThrowsInvalidOperationException(string? connStr)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connStr
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    [Fact]
    public void ValidateProduction_DefaultGatewaySecrets_ThrowInvalidOperationException()
    {
        var (configPayOs, envMock1) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:Gateways:PayOS:WebhookSecret"] = PaymentWebhookVerifier.DefaultPayOSSecret
        });
        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(configPayOs, envMock1.Object, client: false));
        Assert.Contains("PayOS webhook secret cannot be the default placeholder key", ex1.Message);

        var (configVnPay, envMock2) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:Gateways:VNPay:HashSecret"] = PaymentWebhookVerifier.DefaultVNPaySecret
        });
        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(configVnPay, envMock2.Object, client: false));
        Assert.Contains("VNPay hash secret cannot be the default placeholder key", ex2.Message);
    }

    [Fact]
    public void ValidateProduction_InDevelopmentEnvironment_AllowsDefaultsWithoutException()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cinemaflow-dev-test-{Guid.NewGuid():N}");
        var dict = new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StreamUrlSigner.DefaultSigningKey,
            ["Payment:WebhookSecret"] = PaymentWebhookVerifier.DefaultWebhookSecret,
            ["ConnectionStrings:Default"] = ""
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Development");
        envMock.SetupGet(e => e.ContentRootPath).Returns(root);

        // In Development, ValidateProduction returns immediately without throwing
        WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false);
    }

    [Fact]
    public void StreamUrlSigner_InProduction_MissingKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = ""
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StreamUrlSigner(config, environment: envMock.Object));
        Assert.Contains("Security:StreamSigningKey", ex.Message);
    }

    [Fact]
    public void StreamUrlSigner_InProduction_DefaultKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StreamUrlSigner.DefaultSigningKey
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StreamUrlSigner(config, environment: envMock.Object));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Fact]
    public void StreamUrlSigner_InProduction_ShortKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = "short_key_under_32_bytes"
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StreamUrlSigner(config, environment: envMock.Object));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void StreamUrlSigner_InProduction_ValidCustomKey_SignsSuccessfully()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = ValidStreamKey
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var signer = new StreamUrlSigner(config, environment: envMock.Object);
        var signed = signer.Sign("https://cinemaflow.vn", 1, 0, "127.0.0.1");

        Assert.NotNull(signed.StreamUrl);
        Assert.Contains("sig=", signed.StreamUrl);
    }

    [Fact]
    public void StreamUrlSigner_InDevelopment_AllowsMissingKeyAndFallsBackToDefault()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = ""
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Development");

        var signer = new StreamUrlSigner(config, environment: envMock.Object);
        var signed = signer.Sign("https://cinemaflow.vn", 1, 0, "127.0.0.1");

        Assert.NotNull(signed.StreamUrl);
    }

    [Fact]
    public void PaymentWebhookVerifier_InProduction_MissingSecret_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = ""
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Contains("Payment webhook secret key", ex.Message);
    }

    [Fact]
    public void PaymentWebhookVerifier_InProduction_DefaultSecret_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = PaymentWebhookVerifier.DefaultWebhookSecret
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Fact]
    public void PaymentWebhookVerifier_InProduction_ShortSecret_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = "too_short_secret_for_prod"
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            verifier.GetSecretKeyForGateway(null));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void PaymentWebhookVerifier_InProduction_ValidCustomKey_VerifiesSuccessfully()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = ValidWebhookSecret
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "PROD-ORDER-001",
            Amount = 100000m,
            Status = "PAID",
            TransactionReference = "TXN-PROD-999"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = verifier.Verify(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void PaymentWebhookVerifier_InDevelopment_AllowsDefaultSecret()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = ""
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns("Development");

        var verifier = new PaymentWebhookVerifier(config, environment: envMock.Object);
        var key = verifier.GetSecretKeyForGateway(null);
        Assert.Equal(PaymentWebhookVerifier.DefaultWebhookSecret, key);
    }

    [Theory]
    [InlineData("short" + "                           ")] // 5 chars + 27 spaces
    [InlineData("   padded_key_under_32_chars   ")]
    public void ValidateProduction_WhitespacePaddedStreamSigningKey_ThrowsInvalidOperationException(string paddedKey)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = paddedKey
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Theory]
    [InlineData("short" + "                           ")]
    [InlineData("   padded_secret_under_32_chars   ")]
    public void ValidateProduction_WhitespacePaddedPaymentWebhookSecret_ThrowsInvalidOperationException(string paddedSecret)
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = paddedSecret
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Fact]
    public void ValidateProduction_DefaultKeyWithWhitespace_ThrowsInvalidOperationException()
    {
        var (configStream, envMock1) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = $"  {StreamUrlSigner.DefaultSigningKey}  "
        });
        var ex1 = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(configStream, envMock1.Object, client: false));
        Assert.Contains("default placeholder key", ex1.Message);

        var (configWebhook, envMock2) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = $"  {PaymentWebhookVerifier.DefaultWebhookSecret}\n"
        });
        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(configWebhook, envMock2.Object, client: false));
        Assert.Contains("default placeholder key", ex2.Message);
    }

    [Fact]
    public void ValidateProduction_FlatEnvironmentVariables_RecognizedWhenAppsettingsEmpty()
    {
        var (config, envMock) = CreateProductionConfig(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = "",
            ["STREAM_SIGNING_KEY"] = ValidStreamKey,
            ["ConnectionStrings:Default"] = "",
            ["DATABASE_CONNECTION_STRING"] = ValidConnectionString,
            ["Payment:WebhookSecret"] = "",
            ["PAYMENT_WEBHOOK_SECRET"] = ValidWebhookSecret
        });

        // Should not throw because flat env vars take effect
        WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false);
    }
}
