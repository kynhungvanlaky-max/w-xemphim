using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Requests;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class Milestone3SecurityAndResilienceTests
{
    private static (PaymentService service, TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreatePaymentService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-m3-security-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var envMock = new Mock<IHostEnvironment>();
        var configMock = new Mock<IConfiguration>();
        var service = new PaymentService(factory, TimeProvider.System, envMock.Object, configMock.Object);
        return (service, factory, options);
    }

    [Fact]
    public async Task PaymentService_CreatePayment_InactivePlan_ReturnsNull()
    {
        var (service, factory, options) = CreatePaymentService();
        var userId = Guid.NewGuid();

        await using (var ctx = new AppDbContext(options))
        {
            ctx.Users.Add(new UserEntity
            {
                Id = userId,
                Email = "test@cinema.test",
                FullName = "Tester",
                NormalizedEmail = "TEST@CINEMA.TEST",
                PasswordSalt = [1],
                PasswordHash = [2],
                CreatedAtUtc = DateTime.UtcNow
            });
            ctx.VipPlans.Add(new VipPlanEntity
            {
                Id = "vip-archived",
                Name = "Gói Cũ Hết Hạn",
                Description = "Gói hết hạn",
                DurationDays = 30,
                PriceVnd = 50000m,
                IsActive = false // Deactivated
            });
            await ctx.SaveChangesAsync();
        }

        var req = new CreatePaymentRequest
        {
            PlanId = "vip-archived",
            Method = PaymentMethod.BankTransfer
        };

        var resp = await service.CreatePaymentAsync(userId, req);
        Assert.Null(resp);
    }

    [Fact]
    public async Task PaymentService_ValidateCoupon_InactivePlan_ReturnsInvalid()
    {
        var (service, factory, options) = CreatePaymentService();

        await using (var ctx = new AppDbContext(options))
        {
            ctx.VipPlans.Add(new VipPlanEntity
            {
                Id = "vip-disabled",
                Name = "Gói Vô Hiệu Hoá",
                Description = "Đã tắt",
                DurationDays = 30,
                PriceVnd = 50000m,
                IsActive = false
            });
            await ctx.SaveChangesAsync();
        }

        var req = new ApplyCouponRequest
        {
            PlanId = "vip-disabled",
            CouponCode = "CINEMA2026"
        };

        var resp = await service.ValidateCouponAsync(req);
        Assert.False(resp.IsValid);
        Assert.Contains("Gói VIP không hợp lệ hoặc đã ngừng hoạt động", resp.Message);
    }

    [Fact]
    public void PaymentEntity_Status_HasConcurrencyTokenConfigured()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("test-model-concurrency")
            .Options;
        using var ctx = new AppDbContext(options);
        var entityType = ctx.Model.FindEntityType(typeof(PaymentEntity));
        Assert.NotNull(entityType);
        var statusProp = entityType.FindProperty(nameof(PaymentEntity.Status));
        Assert.NotNull(statusProp);
        Assert.True(statusProp.IsConcurrencyToken);
    }

    [Theory]
    [InlineData("203.0.113.195, 70.41.3.18, 150.172.238.178", "203.0.113.195")]
    [InlineData("198.51.100.42", "198.51.100.42")]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8:85a3::8a2e:370:7334")]
    public void ApiRateLimits_GetClientIpAddress_ExtractsValidXForwardedForIp(string xffHeader, string expectedIp)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-For"] = xffHeader;

        var extracted = ApiRateLimits.GetClientIpAddress(httpContext);
        Assert.Equal(expectedIp, extracted);
    }

    [Fact]
    public void ApiRateLimits_GetClientIpAddress_UsesXRealIpWhenNoXff()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Real-IP"] = "192.0.2.1";

        var extracted = ApiRateLimits.GetClientIpAddress(httpContext);
        Assert.Equal("192.0.2.1", extracted);
    }

    [Fact]
    public void ApiRateLimits_GetClientIpAddress_FallsBackToRemoteIpOnInvalidHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-For"] = "malicious-injection-string, not-an-ip";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        var extracted = ApiRateLimits.GetClientIpAddress(httpContext);
        Assert.Equal("10.0.0.5", extracted);
    }

    [Fact]
    public void ApiRateLimits_GetClientIpAddress_ReturnsUnknownWhenNoIpAvailable()
    {
        var httpContext = new DefaultHttpContext();
        // No headers and no RemoteIpAddress

        var extracted = ApiRateLimits.GetClientIpAddress(httpContext);
        Assert.Equal("unknown", extracted);
    }
}
