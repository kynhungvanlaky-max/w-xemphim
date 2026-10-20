using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Requests;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM1ProductionSecurityEmpiricalTests
{
    private const string StrongKey32 = "Adversarial_StreamKey_Strict_32_Bytes!";
    private const string StrongKey64 = "Adversarial_StreamKey_Strict_64_Bytes_Longer_Secure_Random_String_1234567890!";
    private const string StrongWebhookKey = "Adversarial_WebhookSecret_Strict_64_Bytes_Longer_Secure_Random_String_1234567890!";

    private static IHostEnvironment MockEnv(string envName)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.SetupGet(e => e.EnvironmentName).Returns(envName);
        return mock.Object;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void StreamUrlSigner_Production_MissingOrEmptyKey_ThrowsInvalidOperationException(string? emptyKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = emptyKey
        }).Build();

        var env = MockEnv("Production");

        var ex = Assert.Throws<InvalidOperationException>(() => new StreamUrlSigner(config, environment: env));
        Assert.Contains("Security:StreamSigningKey", ex.Message);
        Assert.Contains("Production", ex.Message);
    }

    [Fact]
    public void StreamUrlSigner_Production_DefaultSigningKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StreamUrlSigner.DefaultSigningKey
        }).Build();

        var env = MockEnv("Production");

        var ex = Assert.Throws<InvalidOperationException>(() => new StreamUrlSigner(config, environment: env));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1234567890")]
    [InlineData("1234567890123456789012345678901")] // Exactly 31 chars
    public void StreamUrlSigner_Production_KeyUnder32Bytes_ThrowsInvalidOperationException(string shortKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = shortKey
        }).Build();

        var env = MockEnv("Production");

        var ex = Assert.Throws<InvalidOperationException>(() => new StreamUrlSigner(config, environment: env));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Theory]
    [InlineData(StrongKey32)]
    [InlineData(StrongKey64)]
    public void StreamUrlSigner_Production_ValidKey_InstantiatesAndSignsSuccessfully(string validKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = validKey
        }).Build();

        var env = MockEnv("Production");

        var signer = new StreamUrlSigner(config, environment: env);
        var signed = signer.Sign("https://cinema.example.com", 42, 10, "192.168.1.50");

        Assert.NotNull(signed.StreamUrl);
        Assert.Contains("episodeId=42", signed.StreamUrl);
        Assert.Contains("pos=10", signed.StreamUrl);
        Assert.Contains("sig=", signed.StreamUrl);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Local")]
    public void StreamUrlSigner_NonProduction_AllowsMissingKey_FallsBackToDefault(string envName)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = ""
        }).Build();

        var env = MockEnv(envName);

        var signer = new StreamUrlSigner(config, environment: env);
        var signed = signer.Sign("https://dev.example.com", 1, 0, "127.0.0.1");

        Assert.NotNull(signed.StreamUrl);

        // Verify that DefaultSigningKey was used
        var defaultSigner = new StreamUrlSigner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StreamUrlSigner.DefaultSigningKey
        }).Build(), environment: env);

        var ipHash = signer.ComputeIpHash("127.0.0.1");
        var expectedIpHash = defaultSigner.ComputeIpHash("127.0.0.1");
        Assert.Equal(expectedIpHash, ipHash);
    }

    [Fact]
    public void StreamUrlSigner_Adversarial_TamperedSignature_IsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var signer = new StreamUrlSigner(config);

        var ip = "10.0.0.99";
        var ipHash = signer.ComputeIpHash(ip);
        var exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var validSig = signer.ComputeSignature(100, 1, exp, ipHash);

        // Corrupt signature (flip first char)
        var tamperedChar = validSig[0] == 'a' ? 'b' : 'a';
        var tamperedSig = tamperedChar + validSig[1..];

        var result = signer.Validate(100, 1, exp, ipHash, tamperedSig, ip);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void StreamUrlSigner_Adversarial_ExpiredTimestamp_IsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var signer = new StreamUrlSigner(config);

        var ip = "10.0.0.99";
        var ipHash = signer.ComputeIpHash(ip);
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var sig = signer.ComputeSignature(100, 1, expiredTimestamp, ipHash);

        var result = signer.Validate(100, 1, expiredTimestamp, ipHash, sig, ip);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.Expired, result.Status);
    }

    [Fact]
    public void StreamUrlSigner_Adversarial_MismatchedIp_IsRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var signer = new StreamUrlSigner(config);

        var originalIp = "10.0.0.99";
        var attackingIp = "10.0.0.100";
        var ipHash = signer.ComputeIpHash(originalIp);
        var exp = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var sig = signer.ComputeSignature(100, 1, exp, ipHash);

        var result = signer.Validate(100, 1, exp, ipHash, sig, attackingIp);
        Assert.False(result.IsValid);
        Assert.Equal(StreamValidationStatus.IpMismatch, result.Status);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("::1", "127.0.0.1")]
    [InlineData("0:0:0:0:0:0:0:1", "127.0.0.1")]
    [InlineData(null, "127.0.0.1")]
    [InlineData("", "127.0.0.1")]
    public void StreamUrlSigner_IpNormalization_EmpiricallyConsistent(string? inputIp, string? clientIp)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var signer = new StreamUrlSigner(config);

        var ipHash = signer.ComputeIpHash(inputIp);
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var sig = signer.ComputeSignature(10, 0, exp, ipHash);

        var res = signer.Validate(10, 0, exp, ipHash, sig, clientIp);
        Assert.True(res.IsValid);
        Assert.Equal(StreamValidationStatus.Valid, res.Status);
    }

    [Fact]
    public void StreamUrlSigner_StressTest_ConcurrentSigningAndValidation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var env = MockEnv("Production");
        var signer = new StreamUrlSigner(config, environment: env);

        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var episodeId = (i % 20) + 1;
            var position = i * 10;
            var clientIp = $"192.168.1.{(i % 250) + 1}";

            var signed = signer.Sign("https://cinema.test", episodeId, position, clientIp, TimeSpan.FromMinutes(30));
            var ipHash = signer.ComputeIpHash(clientIp);
            var exp = signed.ExpiresAt.ToUnixTimeSeconds();
            var sig = signer.ComputeSignature(episodeId, position, exp, ipHash);

            var validation = signer.Validate(episodeId, position, exp, ipHash, sig, clientIp);
            if (!validation.IsValid)
            {
                failures.Add($"Iteration {i} failed: {validation.ErrorMessage}");
            }
        });

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PaymentWebhookVerifier_Production_MissingSecret_ThrowsInvalidOperationException(string? missingSecret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = missingSecret
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Contains("Payment webhook secret key is not configured in Production", ex.Message);
    }

    [Fact]
    public void PaymentWebhookVerifier_Production_DefaultWebhookSecret_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = PaymentWebhookVerifier.DefaultWebhookSecret
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway(null));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Theory]
    [InlineData(PaymentWebhookVerifier.DefaultPayOSSecret)]
    [InlineData(PaymentWebhookVerifier.DefaultWebhookSecret)]
    [InlineData(PaymentWebhookVerifier.DefaultVNPaySecret)]
    public void PaymentWebhookVerifier_Production_PayOSDefaultSecrets_ThrowsInvalidOperationException(string defaultKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:PayOS:WebhookSecret"] = defaultKey
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Theory]
    [InlineData(PaymentWebhookVerifier.DefaultVNPaySecret)]
    [InlineData(PaymentWebhookVerifier.DefaultWebhookSecret)]
    [InlineData(PaymentWebhookVerifier.DefaultPayOSSecret)]
    public void PaymentWebhookVerifier_Production_VNPayDefaultSecrets_ThrowsInvalidOperationException(string defaultKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:VNPay:HashSecret"] = defaultKey
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway("VNPay"));
        Assert.Contains("default placeholder key", ex.Message);
    }

    [Fact]
    public void PaymentWebhookVerifier_Production_GatewayKeyUnder32Bytes_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:Gateways:PayOS:WebhookSecret"] = "too_short_gateway_secret"
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway("PayOS"));
        Assert.Contains("at least 32 characters long", ex.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void PaymentWebhookVerifier_NonProduction_MissingKeys_FallsBackToDefault(string envName)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = ""
        }).Build();

        var env = MockEnv(envName);
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var key = verifier.GetSecretKeyForGateway("PayOS");
        Assert.Equal(PaymentWebhookVerifier.DefaultWebhookSecret, key);
    }

    [Fact]
    public void PaymentWebhookVerifier_Adversarial_TamperedPayload_FailsVerification()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = StrongWebhookKey
        }).Build();

        var verifier = new PaymentWebhookVerifier(config);
        var req = new PaymentWebhookRequest
        {
            OrderCode = "ORDER_12345",
            Amount = 199000m,
            Status = "PAID",
            TransactionReference = "TRANS_98765",
            Gateway = "PayOS",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        req.Signature = verifier.ComputeSignature(req);

        // 1. Valid request succeeds
        var validResult = verifier.Verify(req);
        Assert.True(validResult.IsValid);

        // 2. Tampered amount (199000 -> 19000)
        var tamperedReq = new PaymentWebhookRequest
        {
            OrderCode = req.OrderCode,
            Amount = 19000m, // Tampered!
            Status = req.Status,
            TransactionReference = req.TransactionReference,
            Gateway = req.Gateway,
            Timestamp = req.Timestamp,
            Signature = req.Signature
        };
        var tamperedResult = verifier.Verify(tamperedReq);
        Assert.False(tamperedResult.IsValid);
        Assert.Equal(WebhookVerificationStatus.InvalidSignature, tamperedResult.Status);

        // 3. Tampered status
        var statusTamperedReq = new PaymentWebhookRequest
        {
            OrderCode = req.OrderCode,
            Amount = req.Amount,
            Status = "FAILED", // Tampered!
            TransactionReference = req.TransactionReference,
            Gateway = req.Gateway,
            Timestamp = req.Timestamp,
            Signature = req.Signature
        };
        Assert.False(verifier.Verify(statusTamperedReq).IsValid);
    }

    [Fact]
    public void PaymentWebhookVerifier_Adversarial_MissingOrMalformedInputs()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = StrongWebhookKey
        }).Build();
        var verifier = new PaymentWebhookVerifier(config);

        // Null request
        Assert.Equal(WebhookVerificationStatus.MalformedPayload, verifier.Verify(null!).Status);

        // Missing signature
        var noSig = new PaymentWebhookRequest { OrderCode = "ORD1", Amount = 100m, Status = "PAID" };
        Assert.Equal(WebhookVerificationStatus.MissingSignature, verifier.Verify(noSig).Status);

        // Empty OrderCode
        var noOrder = new PaymentWebhookRequest { OrderCode = "", Amount = 100m, Status = "PAID", Signature = "deadbeef" };
        Assert.Equal(WebhookVerificationStatus.MalformedPayload, verifier.Verify(noOrder).Status);
    }

    [Fact]
    public void PaymentWebhookVerifier_StressTest_ConcurrentVerifications()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = StrongWebhookKey
        }).Build();
        var verifier = new PaymentWebhookVerifier(config);

        var errors = new ConcurrentBag<string>();

        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var req = new PaymentWebhookRequest
            {
                OrderCode = $"ORD_STRESS_{i}",
                Amount = 10000m * (i + 1),
                Status = "PAID",
                TransactionReference = $"REF_{i}_{Guid.NewGuid():N}",
                Gateway = i % 2 == 0 ? "PayOS" : "VNPay",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            req.Signature = verifier.ComputeSignature(req);

            var verification = verifier.Verify(req);
            if (!verification.IsValid)
            {
                errors.Add($"Failed verification on stress index {i}");
            }
        });

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("STREAM_SIGNING_KEY", StrongKey32)]
    public void StreamUrlSigner_Production_ResolvesFlatEnvironmentVariable(string envKey, string envVal)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [envKey] = envVal
        }).Build();

        var env = MockEnv("Production");
        var signer = new StreamUrlSigner(config, environment: env);
        var signed = signer.Sign("https://cinema.test", 1, 0, "127.0.0.1");

        Assert.NotNull(signed.StreamUrl);
    }

    [Theory]
    [InlineData("PAYOS_WEBHOOK_SECRET", "payos", StrongWebhookKey)]
    [InlineData("PAYOS_WEBHOOK_SECRET", "PAYOS", StrongWebhookKey)]
    [InlineData("VNPAY_HASH_SECRET", "vnpay", StrongWebhookKey)]
    [InlineData("VNPAY_HASH_SECRET", "VNPay", StrongWebhookKey)]
    [InlineData("PAYMENT_WEBHOOK_SECRET", null, StrongWebhookKey)]
    public void PaymentWebhookVerifier_Production_ResolvesFlatEnvironmentVariablesAndCaseInsensitiveGateways(
        string envKey, string? gateway, string envVal)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [envKey] = envVal
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);
        var resolvedKey = verifier.GetSecretKeyForGateway(gateway);

        Assert.Equal(envVal, resolvedKey);
    }

    [Fact]
    public void PaymentWebhookVerifier_Production_UnknownGateway_FallsBackToMasterKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = StrongWebhookKey
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);
        var resolvedKey = verifier.GetSecretKeyForGateway("UnknownGateway");

        Assert.Equal(StrongWebhookKey, resolvedKey);
    }

    [Fact]
    public void PaymentWebhookVerifier_Production_UnknownGatewayWithoutMasterKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = ""
        }).Build();

        var env = MockEnv("Production");
        var verifier = new PaymentWebhookVerifier(config, environment: env);

        var ex = Assert.Throws<InvalidOperationException>(() => verifier.GetSecretKeyForGateway("UnknownGateway"));
        Assert.Contains("Payment webhook secret key is not configured in Production", ex.Message);
    }

    [Fact]
    public void StreamUrlSigner_Adversarial_DifferentSignatureLength_FailsGracefullyWithoutException()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:StreamSigningKey"] = StrongKey32
        }).Build();
        var signer = new StreamUrlSigner(config);

        var ip = "127.0.0.1";
        var ipHash = signer.ComputeIpHash(ip);
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

        // Pass short signature
        var res1 = signer.Validate(1, 0, exp, ipHash, "abc", ip);
        Assert.False(res1.IsValid);
        Assert.Equal(StreamValidationStatus.InvalidSignature, res1.Status);

        // Pass overly long signature
        var res2 = signer.Validate(1, 0, exp, ipHash, new string('f', 128), ip);
        Assert.False(res2.IsValid);
        Assert.Equal(StreamValidationStatus.InvalidSignature, res2.Status);
    }
}

