using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using WebXemPhim.Shared.Security;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class PaymentWebhookTests
{
    private const string TestWebhookSecret = "CinemaFlow_Test_Secret_Key_At_Least_32_Bytes_Long_2026!";

    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-webhook-tests-{Guid.NewGuid():N}")
            .Options;

    private static (TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreateContextFactory()
    {
        var options = CreateOptions();
        return (new TestDbContextFactory(options), options);
    }

    private static IConfiguration CreateConfig(Dictionary<string, string?>? extra = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Payment:WebhookSecret"] = TestWebhookSecret,
            ["Payment:Gateways:PayOS:WebhookSecret"] = "PayOS_Specific_Secret_2026_At_Least_32_Bytes!",
            ["Payment:Gateways:VNPay:HashSecret"] = "VNPay_Specific_Secret_2026_At_Least_32_Bytes!",
            ["Security:AllowDemoPayments"] = "false"
        };
        if (extra is not null)
        {
            foreach (var kvp in extra)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static async Task<UserEntity> SeedUserAsync(AppDbContext context, string email, MembershipTier tier = MembershipTier.Free, DateTime? vipExpiry = null)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Webhook Test User",
            PasswordHash = [1, 2, 3],
            PasswordSalt = [4, 5, 6],
            Membership = tier,
            VipExpiresAtUtc = vipExpiry,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<PaymentEntity> SeedPaymentAsync(
        AppDbContext context,
        Guid userId,
        string orderCode,
        decimal amount = 79000m,
        string planId = "vip-30",
        PaymentStatus status = PaymentStatus.Pending)
    {
        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderCode = orderCode,
            UserId = userId,
            PlanId = planId,
            PlanName = "Một tháng",
            Amount = amount,
            Method = PaymentMethod.BankTransfer,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            PaidAtUtc = status == PaymentStatus.Completed ? DateTime.UtcNow : null
        };
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment;
    }

    // ==========================================
    // 1. UNIT TESTS: PaymentWebhookVerifier
    // ==========================================

    [Fact]
    public void Verify_ValidSignature_ReturnsSuccess()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORDER-001",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TXN-998877",
            Gateway = "PayOS",
            Timestamp = 1735689600
        };

        request.Signature = verifier.ComputeSignature(request);
        var result = verifier.Verify(request);

        Assert.True(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.Valid, result.Status);
    }

    [Fact]
    public void Verify_TamperedAmount_ReturnsInvalidSignature()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORDER-002",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TXN-001",
            Signature = string.Empty
        };

        request.Signature = verifier.ComputeSignature(request);
        request.Amount = 1000m; // Tampered

        var result = verifier.Verify(request);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_TamperedOrderCode_ReturnsInvalidSignature()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORIGINAL-001",
            Amount = 79000m,
            Status = "PAID"
        };

        request.Signature = verifier.ComputeSignature(request);
        request.OrderCode = "CF-TAMPERED-002";

        var result = verifier.Verify(request);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_TamperedStatus_ReturnsInvalidSignature()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORDER-003",
            Amount = 79000m,
            Status = "FAILED"
        };

        request.Signature = verifier.ComputeSignature(request);
        request.Status = "PAID";

        var result = verifier.Verify(request);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void Verify_MissingOrEmptySignature_ReturnsMissingSignature()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORDER-004",
            Amount = 79000m,
            Status = "PAID",
            Signature = "   "
        };

        var result = verifier.Verify(request);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationStatus.MissingSignature, result.Status);
    }

    [Fact]
    public void Verify_HexCaseInsensitive_AcceptsBothUpperAndLower()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-ORDER-005",
            Amount = 199000m,
            Status = "COMPLETED"
        };

        var sig = verifier.ComputeSignature(request);
        request.Signature = sig.ToUpperInvariant();

        var result = verifier.Verify(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Verify_GatewaySpecificSecret_UsesConfiguredKey()
    {
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-GATEWAY-001",
            Amount = 79000m,
            Status = "PAID",
            Gateway = "PayOS"
        };

        var correctSig = verifier.ComputeSignature(request);
        request.Signature = correctSig;

        var result = verifier.Verify(request);
        Assert.True(result.IsValid);

        // If signed with fallback master secret instead of PayOS secret, it should fail
        var masterSig = verifier.ComputeSignature(request, secretKey: TestWebhookSecret);
        request.Signature = masterSig;

        var failedResult = verifier.Verify(request);
        Assert.False(failedResult.IsValid);
    }

    // ==========================================
    // 2. INTEGRATION TESTS: PaymentService.ProcessWebhookAsync
    // ==========================================

    [Fact]
    public async Task ProcessWebhook_WithValidSignature_ActivatesVipAndCompletesPayment()
    {
        var (factory, options) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "valid_vip@cinema.test", MembershipTier.Free);
            await SeedPaymentAsync(db, user.Id, "CF-VALID-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-VALID-001",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TXN-BANK-1",
            Gateway = "VNPay"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.Success, result.Status);
        Assert.NotNull(result.Payment);
        Assert.Equal(PaymentStatus.Completed, result.Payment.Status);

        using (var verifyDb = factory.CreateDbContext())
        {
            var updatedPayment = await verifyDb.Payments.Include(p => p.User).FirstAsync(p => p.OrderCode == "CF-VALID-001");
            Assert.Equal(PaymentStatus.Completed, updatedPayment.Status);
            Assert.NotNull(updatedPayment.PaidAtUtc);
            Assert.Equal("TXN-BANK-1", updatedPayment.TransactionReference);

            Assert.Equal(MembershipTier.Vip, updatedPayment.User.Membership);
            Assert.NotNull(updatedPayment.User.VipExpiresAtUtc);
            Assert.True(updatedPayment.User.VipExpiresAtUtc > DateTime.UtcNow.AddDays(29));

            var notification = await verifyDb.Notifications.FirstOrDefaultAsync(n => n.UserId == updatedPayment.UserId);
            Assert.NotNull(notification);
            Assert.Contains("Nâng cấp VIP thành công", notification.Title);
        }
    }

    [Fact]
    public async Task ProcessWebhook_WithInvalidSignature_Returns403AndDoesNotExtendVip()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "forged_vip@cinema.test", MembershipTier.Free);
            userId = user.Id;
            await SeedPaymentAsync(db, user.Id, "CF-FORGED-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-FORGED-001",
            Amount = 79000m,
            Status = "PAID",
            Signature = "forged_hex_signature_abcdef1234567890abcdef1234567890abcdef1234567890"
        };

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.InvalidSignature, result.Status);
        Assert.Null(result.Payment);

        using (var verifyDb = factory.CreateDbContext())
        {
            var payment = await verifyDb.Payments.FirstAsync(p => p.OrderCode == "CF-FORGED-001");
            Assert.Equal(PaymentStatus.Pending, payment.Status);
            Assert.Null(payment.PaidAtUtc);

            var user = await verifyDb.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(MembershipTier.Free, user.Membership);
            Assert.Null(user.VipExpiresAtUtc);
        }
    }

    [Fact]
    public async Task ProcessWebhook_DuplicateCall_IsIdempotentAndDoesNotDoubleExtendVip()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "idempotent@cinema.test", MembershipTier.Free);
            userId = user.Id;
            await SeedPaymentAsync(db, user.Id, "CF-IDEM-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-IDEM-001",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TXN-DUP-01"
        };
        request.Signature = verifier.ComputeSignature(request);

        // Call 1
        var result1 = await service.ProcessWebhookAsync(request);
        Assert.Equal(PaymentWebhookProcessStatus.Success, result1.Status);

        DateTime? firstExpiry;
        int notificationCount1;
        using (var db1 = factory.CreateDbContext())
        {
            var user1 = await db1.Users.FirstAsync(u => u.Id == userId);
            firstExpiry = user1.VipExpiresAtUtc;
            notificationCount1 = await db1.Notifications.CountAsync(n => n.UserId == userId);
        }

        // Call 2 (Duplicate replay)
        var result2 = await service.ProcessWebhookAsync(request);
        Assert.Equal(PaymentWebhookProcessStatus.AlreadyProcessed, result2.Status);
        Assert.NotNull(result2.Payment);

        using (var db2 = factory.CreateDbContext())
        {
            var user2 = await db2.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(firstExpiry, user2.VipExpiresAtUtc); // Must NOT double extend
            var notificationCount2 = await db2.Notifications.CountAsync(n => n.UserId == userId);
            Assert.Equal(notificationCount1, notificationCount2); // Must NOT duplicate notifications
        }
    }

    [Fact]
    public async Task ProcessWebhook_UnknownOrderCode_ReturnsNotFound()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-UNKNOWN-999999",
            Amount = 79000m,
            Status = "PAID"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.OrderNotFound, result.Status);
        Assert.Contains("CF-UNKNOWN-999999", result.Message);
    }

    [Fact]
    public async Task ProcessWebhook_ActiveVipUser_StacksDurationOnTopOfExistingExpiry()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        var existingExpiry = DateTime.UtcNow.AddDays(15);
        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "stack_vip@cinema.test", MembershipTier.Vip, existingExpiry);
            userId = user.Id;
            await SeedPaymentAsync(db, user.Id, "CF-STACK-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-STACK-001",
            Amount = 79000m,
            Status = "PAID"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);
        Assert.Equal(PaymentWebhookProcessStatus.Success, result.Status);

        using (var verifyDb = factory.CreateDbContext())
        {
            var user = await verifyDb.Users.FirstAsync(u => u.Id == userId);
            // Must add 30 days onto existing 15 days = ~45 days
            var totalDays = (user.VipExpiresAtUtc!.Value - DateTime.UtcNow).TotalDays;
            Assert.True(totalDays >= 44.5 && totalDays <= 45.5);
        }
    }

    [Fact]
    public async Task ProcessWebhook_AmountMismatch_ReturnsAmountMismatch()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "mismatch@cinema.test", MembershipTier.Free);
            await SeedPaymentAsync(db, user.Id, "CF-MISMATCH-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        // Webhook sends 1000 instead of 79000
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-MISMATCH-001",
            Amount = 1000m,
            Status = "PAID"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.AmountMismatch, result.Status);

        using (var verifyDb = factory.CreateDbContext())
        {
            var payment = await verifyDb.Payments.FirstAsync(p => p.OrderCode == "CF-MISMATCH-001");
            Assert.Equal(PaymentStatus.Pending, payment.Status);
        }
    }

    [Fact]
    public async Task ProcessWebhook_DuplicateTransactionReference_ReturnsDuplicateTransaction()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "dup_trx@cinema.test", MembershipTier.Free);
            var completedPayment = await SeedPaymentAsync(db, user.Id, "CF-COMPLETED-001", 79000m, "vip-30", PaymentStatus.Completed);
            completedPayment.TransactionReference = "BANK-TRX-ALREADY-USED";
            await SeedPaymentAsync(db, user.Id, "CF-PENDING-002", 79000m, "vip-30", PaymentStatus.Pending);
            await db.SaveChangesAsync();
        }

        // Second payment attempts to use same TransactionReference
        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-PENDING-002",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "BANK-TRX-ALREADY-USED"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.DuplicateTransactionReference, result.Status);
    }

    [Fact]
    public async Task ProcessWebhook_GatewayFailedStatus_MarksPaymentFailed()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "fail_status@cinema.test", MembershipTier.Free);
            userId = user.Id;
            await SeedPaymentAsync(db, user.Id, "CF-FAILED-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-FAILED-001",
            Amount = 79000m,
            Status = "FAILED"
        };
        request.Signature = verifier.ComputeSignature(request);

        var result = await service.ProcessWebhookAsync(request);

        Assert.Equal(PaymentWebhookProcessStatus.PaymentFailedByGateway, result.Status);

        using (var verifyDb = factory.CreateDbContext())
        {
            var payment = await verifyDb.Payments.FirstAsync(p => p.OrderCode == "CF-FAILED-001");
            Assert.Equal(PaymentStatus.Failed, payment.Status);

            var user = await verifyDb.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(MembershipTier.Free, user.Membership);
            Assert.Null(user.VipExpiresAtUtc);
        }
    }

    [Fact]
    public async Task ProcessWebhook_ConcurrentDuplicateCalls_HandledSafelyWithoutDoubleExtension()
    {
        var (factory, _) = CreateContextFactory();
        var config = CreateConfig();
        var verifier = new PaymentWebhookVerifier(config);
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Testing");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config, verifier);

        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "concurrent_webhook@cinema.test", MembershipTier.Free);
            userId = user.Id;
            await SeedPaymentAsync(db, user.Id, "CF-CONCUR-001", 79000m, "vip-30", PaymentStatus.Pending);
        }

        var request = new PaymentWebhookRequest
        {
            OrderCode = "CF-CONCUR-001",
            Amount = 79000m,
            Status = "PAID",
            TransactionReference = "TXN-CONCUR-1"
        };
        request.Signature = verifier.ComputeSignature(request);

        // Run 5 simultaneous calls
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.ProcessWebhookAsync(request))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Every call must succeed (either Success or AlreadyProcessed)
        Assert.All(results, r => Assert.True(
            r.Status is PaymentWebhookProcessStatus.Success or PaymentWebhookProcessStatus.AlreadyProcessed));

        using (var verifyDb = factory.CreateDbContext())
        {
            var user = await verifyDb.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(MembershipTier.Vip, user.Membership);
            // User should have exactly ~30 days, NOT 150 days
            var totalDays = (user.VipExpiresAtUtc!.Value - DateTime.UtcNow).TotalDays;
            Assert.True(totalDays >= 29 && totalDays <= 31);
        }
    }

    // ==========================================
    // 3. PRODUCTION GUARDRAILS & RUNTIME TESTS
    // ==========================================

    [Fact]
    public void ProductionGuardrail_AllowDemoPayments_ThrowsOnStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "cinemaflow-production-policy");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "cinema.example.test",
            ["Security:DataProtectionKeyPath"] = Path.Combine(root, "private-keys"),
            ["Security:AllowDemoPayments"] = "true"
        }).Build();

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(x => x.EnvironmentName).Returns("Production");
        envMock.SetupGet(x => x.ContentRootPath).Returns(root);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WebSecurityExtensions.ValidateProduction(config, envMock.Object, client: false));

        Assert.Contains("forbidden in Production", ex.Message);
    }

    [Fact]
    public async Task CreatePayment_InProductionEnvironment_AlwaysCreatesPendingPaymentEvenIfAllowDemoConfigured()
    {
        var (factory, _) = CreateContextFactory();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:AllowDemoPayments"] = "true" // Injected but environment is Production
        }).Build();

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, config);

        Guid userId;
        using (var db = factory.CreateDbContext())
        {
            var user = await SeedUserAsync(db, "prod_user@cinema.test", MembershipTier.Free);
            userId = user.Id;
        }

        var result = await service.CreatePaymentAsync(userId, new CreatePaymentRequest
        {
            PlanId = "vip-30",
            Method = PaymentMethod.BankTransfer
        });

        Assert.NotNull(result);
        Assert.Equal(PaymentStatus.Pending, result.Status); // Must remain Pending!
        Assert.Null(result.PaidAt);

        using (var verifyDb = factory.CreateDbContext())
        {
            var user = await verifyDb.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(MembershipTier.Free, user.Membership); // VIP must NOT be activated!
        }
    }

    // ==========================================
    // 4. CONTROLLER ACTION HTTP STATUS TESTS
    // ==========================================

    [Fact]
    public async Task Controller_ProcessWebhook_MapsStatusCodesAccurately()
    {
        var paymentServiceMock = new Mock<IPaymentService>();

        // Scenario 1: Malformed body (Missing OrderCode)
        var controller = new PaymentsController(paymentServiceMock.Object);
        var badReqResult = await controller.ProcessWebhook(new PaymentWebhookRequest
        {
            OrderCode = "",
            Signature = "sig",
            Amount = 79000,
            Status = "PAID"
        });
        Assert.IsType<BadRequestObjectResult>(badReqResult);

        // Scenario 2: Invalid signature -> 403 Forbidden
        paymentServiceMock.Setup(s => s.ProcessWebhookAsync(It.Is<PaymentWebhookRequest>(r => r.OrderCode == "FORBIDDEN")))
            .ReturnsAsync(PaymentWebhookResult.InvalidSig());

        var forbidResult = await controller.ProcessWebhook(new PaymentWebhookRequest
        {
            OrderCode = "FORBIDDEN",
            Signature = "sig",
            Amount = 79000,
            Status = "PAID"
        });
        var objectResult = Assert.IsType<ObjectResult>(forbidResult);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);

        // Scenario 3: Not Found -> 404
        paymentServiceMock.Setup(s => s.ProcessWebhookAsync(It.Is<PaymentWebhookRequest>(r => r.OrderCode == "NOTFOUND")))
            .ReturnsAsync(PaymentWebhookResult.NotFound());

        var notFoundResult = await controller.ProcessWebhook(new PaymentWebhookRequest
        {
            OrderCode = "NOTFOUND",
            Signature = "sig",
            Amount = 79000,
            Status = "PAID"
        });
        Assert.IsType<NotFoundObjectResult>(notFoundResult);

        // Scenario 4: Success -> 200 OK
        var paymentDto = new PaymentDto { OrderCode = "OK001", Status = PaymentStatus.Completed };
        paymentServiceMock.Setup(s => s.ProcessWebhookAsync(It.Is<PaymentWebhookRequest>(r => r.OrderCode == "OK001")))
            .ReturnsAsync(PaymentWebhookResult.Ok(paymentDto));

        var okResult = await controller.ProcessWebhook(new PaymentWebhookRequest
        {
            OrderCode = "OK001",
            Signature = "sig",
            Amount = 79000,
            Status = "PAID"
        });
        var okObjResult = Assert.IsType<OkObjectResult>(okResult);
        var apiResp = Assert.IsType<ApiResponse<PaymentDto>>(okObjResult.Value);
        Assert.True(apiResp.Success);
    }
}
