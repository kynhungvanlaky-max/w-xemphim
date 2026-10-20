using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class VipPaymentSystemTests
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private static (AdminPaymentsController adminPaymentsController, AdminUsersController adminUsersController, PaymentsController paymentsController, PaymentService paymentService, DbContextOptions<AppDbContext> options) CreateControllers()
    {
        var dbName = $"webxemphim-viptests-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestContextFactory(options);
        var envMock = new Mock<IHostEnvironment>();
        var configMock = new Mock<IConfiguration>();

        var paymentService = new PaymentService(factory, TimeProvider.System, envMock.Object, configMock.Object);
        var adminPaymentsController = new AdminPaymentsController(factory, TimeProvider.System);
        var adminUsersController = new AdminUsersController(factory, TimeProvider.System);
        var paymentsController = new PaymentsController(paymentService);

        return (adminPaymentsController, adminUsersController, paymentsController, paymentService, options);
    }

    [Theory]
    [InlineData("CINEMA2026", "vip-30", 20, 15800, 63200)]
    [InlineData("VIPPRO", "vip-30", 15, 11850, 67150)]
    [InlineData("TRIAN50K", "vip-30", 0, 50000, 29000)]
    [InlineData("TESTVIP", "vip-30", 50, 39500, 39500)]
    public async Task ValidateCoupon_ValidCodes_ReturnsCorrectDiscounts(string couponCode, string planId, decimal expectedPercent, decimal expectedDiscount, decimal expectedFinal)
    {
        var (_, _, _, paymentService, _) = CreateControllers();
        var request = new ApplyCouponRequest { CouponCode = couponCode, PlanId = planId };
        var result = await paymentService.ValidateCouponAsync(request);

        Assert.True(result.IsValid);
        Assert.Equal(expectedPercent, result.DiscountPercent);
        Assert.Equal(expectedDiscount, result.DiscountAmount);
        Assert.Equal(expectedFinal, result.FinalPrice);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task ValidateCoupon_InvalidCode_ReturnsInvalid()
    {
        PaymentService.ResetDefaultPlans();
        var (_, _, _, paymentService, _) = CreateControllers();
        var request = new ApplyCouponRequest { CouponCode = "SUPER_FAKE_COUPON", PlanId = "vip-30" };
        var result = await paymentService.ValidateCouponAsync(request);

        Assert.False(result.IsValid);
        Assert.Equal(0, result.DiscountAmount);
        Assert.Equal(79000, result.FinalPrice);
    }

    [Fact]
    public async Task GetAdminPlans_ReturnsAllFourPlansWithOriginalPrice()
    {
        PaymentService.ResetDefaultPlans();
        var (_, _, _, paymentService, _) = CreateControllers();
        var plans = await paymentService.GetAdminPlansAsync();

        Assert.Equal(4, plans.Count);
        Assert.Contains(plans, p => p.Id == "vip-30");
        Assert.Contains(plans, p => p.Id == "vip-90");
        Assert.Contains(plans, p => p.Id == "vip-180");
        Assert.Contains(plans, p => p.Id == "vip-365");
        Assert.All(plans, p => Assert.True(p.Price > 0));
    }

    [Fact]
    public async Task UpsertPlan_UpdatesPlanInformation()
    {
        try
        {
            var (_, _, _, paymentService, _) = CreateControllers();
            var request = new UpsertVipPlanRequest
            {
                Id = "vip-30",
                Name = "VIP 1 Tháng Ưu Đãi Khủng",
                Price = 49000,
                OriginalPrice = 99000,
                DiscountBadge = "-50%",
                DurationDays = 30,
                Description = "Gói tháng đặc biệt",
                Benefits = ["Xem không quảng cáo", "Ultra HD"],
                IsRecommended = false,
                IsActive = true
            };

            var updated = await paymentService.UpsertPlanAsync(request);

            Assert.NotNull(updated);
            Assert.Equal("VIP 1 Tháng Ưu Đãi Khủng", updated.Name);
            Assert.Equal(49000, updated.Price);
            Assert.Equal(99000, updated.OriginalPrice);
            Assert.Equal("-50%", updated.DiscountBadge);
        }
        finally
        {
            PaymentService.ResetDefaultPlans();
        }
    }

    [Fact]
    public async Task AdminController_RejectPayment_SetsFailedStatusAndAuditsReason()
    {
        var (adminPaymentsController, _, _, _, options) = CreateControllers();
        var paymentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var context = new AppDbContext(options))
        {
            context.Users.Add(new UserEntity
            {
                Id = userId,
                FullName = "Tester",
                Email = "test@cinema.vn",
                NormalizedEmail = "TEST@CINEMA.VN",
                PasswordSalt = [1],
                PasswordHash = [2]
            });
            context.Payments.Add(new PaymentEntity
            {
                Id = paymentId,
                OrderCode = "ORD-TEST-REJECT",
                UserId = userId,
                PlanId = "vip-30",
                PlanName = "VIP 1 Tháng",
                Amount = 59000,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var rejectRequest = new PaymentRejectionRequest
        {
            Reason = "Mã giao dịch ngân hàng không khớp với số dư sao kê."
        };

        var actionResult = await adminPaymentsController.RejectPayment(paymentId, rejectRequest);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<PaymentDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(PaymentStatus.Failed, response.Data?.Status);
        Assert.Contains("Từ chối duyệt:", response.Data?.Note);

        await using (var context = new AppDbContext(options))
        {
            var saved = await context.Payments.FindAsync(paymentId);
            Assert.NotNull(saved);
            Assert.Equal(PaymentStatus.Failed, saved.Status);
            Assert.Contains("Mã giao dịch ngân hàng không khớp", saved.Note);
        }
    }

    [Fact]
    public async Task AdminController_GrantVipToUser_ExtendsMembershipAndSendsNotification()
    {
        var (_, adminUsersController, _, _, options) = CreateControllers();
        var userId = Guid.NewGuid();

        await using (var context = new AppDbContext(options))
        {
            context.Users.Add(new UserEntity
            {
                Id = userId,
                FullName = "Thành viên Thân Thiết",
                Email = "vipuser@cinema.vn",
                NormalizedEmail = "VIPUSER@CINEMA.VN",
                PasswordSalt = [1],
                PasswordHash = [2],
                Membership = MembershipTier.Free
            });
            await context.SaveChangesAsync();
        }

        var grantRequest = new ManualVipGrantRequest
        {
            UserEmailOrUsername = "vipuser@cinema.vn",
            Days = 90,
            Reason = "Tri ân đối tác và thành viên VIP tích cực"
        };

        var actionResult = await adminUsersController.GrantVipToUser(grantRequest);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<UserDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(MembershipTier.Vip, response.Data?.MembershipTier);
        Assert.NotNull(response.Data?.SubscriptionExpiresAt);

        await using (var context = new AppDbContext(options))
        {
            var user = await context.Users.FindAsync(userId);
            Assert.NotNull(user);
            Assert.Equal(MembershipTier.Vip, user.Membership);
            Assert.NotNull(user.VipExpiresAtUtc);
            Assert.True(user.VipExpiresAtUtc > DateTime.UtcNow.AddDays(80));

            var notification = await context.Notifications.FirstOrDefaultAsync(n => n.UserId == userId);
            Assert.NotNull(notification);
            Assert.Contains("Cấp quyền VIP thành công", notification.Title);
            Assert.Contains("90 ngày", notification.Copy);
        }
    }

    [Fact]
    public async Task PaymentsController_ApplyCoupon_ReturnsValidationResult()
    {
        var (_, _, paymentsController, _, _) = CreateControllers();
        var request = new ApplyCouponRequest
        {
            CouponCode = "CINEMA2026",
            PlanId = "vip-30"
        };

        var actionResult = await paymentsController.ApplyCoupon(request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<CouponValidationResultDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.True(response.Data.IsValid);
        Assert.Equal(15800, response.Data.DiscountAmount);
        Assert.Equal(63200, response.Data.FinalPrice);
    }
}
