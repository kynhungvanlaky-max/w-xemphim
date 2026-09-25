using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Services;

public interface IPaymentService
{
    Task<List<VipPlanDto>> GetPlansAsync();
    Task<PaymentDto?> CreatePaymentAsync(Guid userId, CreatePaymentRequest request);
    Task<List<PaymentDto>> GetHistoryAsync(Guid userId);
    Task<CouponValidationResultDto> ValidateCouponAsync(ApplyCouponRequest request);
    Task<List<VipPlanDto>> GetAdminPlansAsync();
    Task<VipPlanDto?> UpsertPlanAsync(UpsertVipPlanRequest request);
}

public sealed class PaymentService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    IConfiguration configuration) : IPaymentService
{
    private static readonly List<VipPlanDto> DefaultPlans =
    [
        new() { Id = "vip-30", Name = "Một tháng", DurationDays = 30, Price = 79000, OriginalPrice = 79000, Description = "Linh hoạt, dễ bắt đầu", Benefits = ["4K HDR", "Không quảng cáo", "Nhiều thiết bị", "Máy chủ ưu tiên"] },
        new() { Id = "vip-90", Name = "Ba tháng", DurationDays = 90, Price = 199000, OriginalPrice = 237000, DiscountBadge = "Tiết kiệm 17%", Description = "Tiết kiệm hơn, trải nghiệm dài hơn", Benefits = ["4K HDR", "Không quảng cáo", "Nhiều thiết bị", "Máy chủ ưu tiên"] },
        new() { Id = "vip-180", Name = "Sáu tháng", DurationDays = 180, Price = 349000, OriginalPrice = 474000, DiscountBadge = "Tiết kiệm 26%", Description = "Bán chạy nhất cho người luyện phim", Benefits = ["4K HDR", "Không quảng cáo", "Nhiều thiết bị", "Máy chủ ưu tiên"] },
        new() { Id = "vip-365", Name = "Một năm", DurationDays = 365, Price = 499000, OriginalPrice = 948000, DiscountBadge = "Tiết kiệm 47%", Description = "Lựa chọn tốt nhất cho người yêu phim", Benefits = ["4K HDR", "Không quảng cáo", "Nhiều thiết bị", "Máy chủ ưu tiên"], IsRecommended = true }
    ];

    private static readonly List<VipPlanDto> Plans = DefaultPlans.Select(ClonePlan).ToList();

    public static void ResetDefaultPlans()
    {
        lock (Plans)
        {
            Plans.Clear();
            Plans.AddRange(DefaultPlans.Select(ClonePlan));
        }
    }

    public Task<List<VipPlanDto>> GetPlansAsync() =>
        Task.FromResult(Plans.Where(p => p.IsActive).Select(ClonePlan).ToList());

    public Task<List<VipPlanDto>> GetAdminPlansAsync() =>
        Task.FromResult(Plans.Select(ClonePlan).ToList());

    public Task<VipPlanDto?> UpsertPlanAsync(UpsertVipPlanRequest request)
    {
        lock (Plans)
        {
            var existing = Plans.FirstOrDefault(p => p.Id.Equals(request.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Name = request.Name.Trim();
                existing.DurationDays = request.DurationDays;
                existing.Price = request.Price;
                existing.OriginalPrice = request.OriginalPrice;
                existing.DiscountBadge = request.DiscountBadge?.Trim();
                existing.Description = request.Description?.Trim() ?? string.Empty;
                existing.Benefits = request.Benefits ?? [];
                existing.IsRecommended = request.IsRecommended;
                existing.IsActive = request.IsActive;
                return Task.FromResult<VipPlanDto?>(ClonePlan(existing));
            }

            var newPlan = new VipPlanDto
            {
                Id = request.Id.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                DurationDays = request.DurationDays,
                Price = request.Price,
                OriginalPrice = request.OriginalPrice,
                DiscountBadge = request.DiscountBadge?.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                Benefits = request.Benefits ?? [],
                IsRecommended = request.IsRecommended,
                IsActive = request.IsActive
            };
            Plans.Add(newPlan);
            return Task.FromResult<VipPlanDto?>(ClonePlan(newPlan));
        }
    }

    public Task<CouponValidationResultDto> ValidateCouponAsync(ApplyCouponRequest request)
    {
        var plan = Plans.FirstOrDefault(candidate => candidate.Id.Equals(request.PlanId, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return Task.FromResult(new CouponValidationResultDto
            {
                IsValid = false,
                Message = "Gói VIP không hợp lệ."
            });
        }

        var code = request.CouponCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var originalPrice = plan.Price;

        return Task.FromResult(code switch
        {
            "CINEMA2026" => new CouponValidationResultDto
            {
                IsValid = true,
                CouponCode = "CINEMA2026",
                DiscountPercent = 20,
                DiscountAmount = originalPrice * 0.20m,
                OriginalPrice = originalPrice,
                FinalPrice = Math.Max(0, originalPrice * 0.80m),
                Message = "Áp dụng thành công mã CINEMA2026: Giảm 20% tổng đơn hàng!"
            },
            "VIPPRO" => new CouponValidationResultDto
            {
                IsValid = true,
                CouponCode = "VIPPRO",
                DiscountPercent = 15,
                DiscountAmount = originalPrice * 0.15m,
                OriginalPrice = originalPrice,
                FinalPrice = Math.Max(0, originalPrice * 0.85m),
                Message = "Áp dụng thành công mã VIPPRO: Giảm 15% tổng đơn hàng!"
            },
            "TRIAN50K" => new CouponValidationResultDto
            {
                IsValid = true,
                CouponCode = "TRIAN50K",
                DiscountPercent = 0,
                DiscountAmount = Math.Min(originalPrice, 50000m),
                OriginalPrice = originalPrice,
                FinalPrice = Math.Max(0, originalPrice - 50000m),
                Message = "Áp dụng thành công mã TRIAN50K: Giảm 50.000đ trực tiếp vào đơn hàng!"
            },
            "TESTVIP" => new CouponValidationResultDto
            {
                IsValid = true,
                CouponCode = "TESTVIP",
                DiscountPercent = 50,
                DiscountAmount = originalPrice * 0.50m,
                OriginalPrice = originalPrice,
                FinalPrice = Math.Max(0, originalPrice * 0.50m),
                Message = "Áp dụng thành công mã TESTVIP: Giảm 50%!"
            },
            _ => new CouponValidationResultDto
            {
                IsValid = false,
                CouponCode = code,
                OriginalPrice = originalPrice,
                FinalPrice = originalPrice,
                Message = "Mã giảm giá không hợp lệ hoặc đã hết lượt áp dụng."
            }
        });
    }

    public async Task<PaymentDto?> CreatePaymentAsync(Guid userId, CreatePaymentRequest request)
    {
        var plan = Plans.FirstOrDefault(candidate => candidate.Id.Equals(request.PlanId, StringComparison.OrdinalIgnoreCase));
        if (plan is null || !Enum.IsDefined(request.Method))
        {
            return null;
        }

        var amount = plan.Price;
        string? note = null;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var coupon = await ValidateCouponAsync(new ApplyCouponRequest
            {
                PlanId = request.PlanId,
                CouponCode = request.CouponCode
            });

            if (coupon.IsValid)
            {
                amount = coupon.FinalPrice;
                note = $"Áp dụng mã giảm giá {coupon.CouponCode} (-{coupon.DiscountAmount:N0}đ)";
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null) return null;

        var demo = environment.IsDevelopment() && configuration.GetValue<bool>("Security:AllowDemoPayments");
        if (demo)
        {
            var startsAt = user.VipExpiresAtUtc > now ? user.VipExpiresAtUtc.Value : now;
            user.Membership = MembershipTier.Vip;
            user.VipExpiresAtUtc = startsAt.AddDays(plan.DurationDays);
        }
        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderCode = $"CF{Guid.NewGuid():N}"[..26],
            UserId = userId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            Amount = amount,
            Method = request.Method,
            Status = demo ? PaymentStatus.Completed : PaymentStatus.Pending,
            CreatedAtUtc = now,
            PaidAtUtc = demo ? now : null,
            VipExpiresAtUtc = demo ? user.VipExpiresAtUtc : null,
            Note = note
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        payment.User = user;
        return MapPayment(payment);
    }

    public async Task<List<PaymentDto>> GetHistoryAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Payments.AsNoTracking()
            .Where(payment => payment.UserId == userId)
            .OrderByDescending(payment => payment.CreatedAtUtc)
            .Select(payment => new PaymentDto
            {
                Id = payment.Id,
                OrderCode = payment.OrderCode,
                UserId = payment.UserId,
                PlanId = payment.PlanId,
                PlanName = payment.PlanName,
                Amount = payment.Amount,
                Method = payment.Method,
                Status = payment.Status,
                CreatedAt = payment.CreatedAtUtc,
                PaidAt = payment.PaidAtUtc,
                VipExpiresAt = payment.VipExpiresAtUtc,
                TransactionReference = payment.TransactionReference,
                Note = payment.Note
            })
            .ToListAsync();
    }

    private static PaymentDto MapPayment(PaymentEntity payment) => new()
    {
        Id = payment.Id,
        OrderCode = payment.OrderCode,
        UserId = payment.UserId,
        UserEmail = payment.User?.Email,
        UserFullName = payment.User?.FullName,
        PlanId = payment.PlanId,
        PlanName = payment.PlanName,
        Amount = payment.Amount,
        Method = payment.Method,
        Status = payment.Status,
        CreatedAt = payment.CreatedAtUtc,
        PaidAt = payment.PaidAtUtc,
        VipExpiresAt = payment.VipExpiresAtUtc,
        TransactionReference = payment.TransactionReference,
        Note = payment.Note
    };

    private static VipPlanDto ClonePlan(VipPlanDto plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        DurationDays = plan.DurationDays,
        Price = plan.Price,
        OriginalPrice = plan.OriginalPrice,
        DiscountBadge = plan.DiscountBadge,
        Description = plan.Description,
        Benefits = [.. plan.Benefits],
        IsRecommended = plan.IsRecommended,
        IsActive = plan.IsActive
    };
}
