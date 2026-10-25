using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
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
    Task<PaymentWebhookResult> ProcessWebhookAsync(PaymentWebhookRequest request);
}

public sealed class PaymentService(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    IConfiguration configuration,
    IPaymentWebhookVerifier? webhookVerifier = null,
    IHubContext<CinemaHub>? hubContext = null) : IPaymentService
{
    private readonly IPaymentWebhookVerifier _webhookVerifier =
        webhookVerifier ?? new PaymentWebhookVerifier(configuration, timeProvider, environment);
    private async Task EnsureSeededAsync(AppDbContext context)
    {
        if (!await context.VipPlans.AnyAsync())
        {
            context.VipPlans.AddRange(
                new VipPlanEntity { Id = "vip-30", Name = "Một tháng", Description = "Linh hoạt, dễ bắt đầu - 30 ngày trải nghiệm xem phim không giới hạn", PriceVnd = 79000m, DurationDays = 30, IsActive = true, DisplayOrder = 1 },
                new VipPlanEntity { Id = "vip-90", Name = "Ba tháng", Description = "Tiết kiệm hơn, trải nghiệm dài hơn - 90 ngày xem phim chất lượng cao", PriceVnd = 199000m, DurationDays = 90, IsActive = true, DisplayOrder = 2 },
                new VipPlanEntity { Id = "vip-180", Name = "Sáu tháng", Description = "Bán chạy nhất cho người luyện phim - 180 ngày đặc quyền VIP", PriceVnd = 349000m, DurationDays = 180, IsActive = true, DisplayOrder = 3 },
                new VipPlanEntity { Id = "vip-365", Name = "Một năm", Description = "Lựa chọn tốt nhất cho người yêu phim - 365 ngày đặc quyền tối thượng", PriceVnd = 499000m, DurationDays = 365, IsActive = true, DisplayOrder = 4 }
            );
            await context.SaveChangesAsync();
        }

        if (!await context.Coupons.AnyAsync())
        {
            var validUntil = timeProvider.GetUtcNow().UtcDateTime.AddYears(5);
            context.Coupons.AddRange(
                new CouponEntity { Code = "CINEMA2026", DiscountType = "percent", DiscountValue = 20m, ValidUntilUtc = validUntil, IsActive = true },
                new CouponEntity { Code = "VIPPRO", DiscountType = "percent", DiscountValue = 15m, ValidUntilUtc = validUntil, IsActive = true },
                new CouponEntity { Code = "TRIAN50K", DiscountType = "fixed", DiscountValue = 50000m, ValidUntilUtc = validUntil, IsActive = true },
                new CouponEntity { Code = "TESTVIP", DiscountType = "percent", DiscountValue = 50m, ValidUntilUtc = validUntil, IsActive = true }
            );
            await context.SaveChangesAsync();
        }
    }

    private static VipPlanDto MapPlan(VipPlanEntity entity)
    {
        var originalPrice = entity.Id switch
        {
            "vip-90" => 237000m,
            "vip-180" => 474000m,
            "vip-365" => 948000m,
            _ => (decimal?)null
        };
        var discountBadge = entity.Id switch
        {
            "vip-90" => "Tiết kiệm 17%",
            "vip-180" => "Tiết kiệm 26%",
            "vip-365" => "Tiết kiệm 47%",
            _ => null
        };
        return new VipPlanDto
        {
            Id = entity.Id,
            Name = entity.Name,
            DurationDays = entity.DurationDays,
            Price = entity.PriceVnd,
            OriginalPrice = originalPrice,
            DiscountBadge = discountBadge,
            Description = entity.Description,
            Benefits = ["4K HDR", "Không quảng cáo", "Nhiều thiết bị", "Máy chủ ưu tiên"],
            IsRecommended = entity.Id == "vip-365",
            IsActive = entity.IsActive
        };
    }

    public static void ResetDefaultPlans()
    {
    }

    public async Task<List<VipPlanDto>> GetPlansAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);
        var entities = await context.VipPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();
        return entities.Select(MapPlan).ToList();
    }

    public async Task<List<VipPlanDto>> GetAdminPlansAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);
        var entities = await context.VipPlans
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();
        return entities.Select(MapPlan).ToList();
    }

    public async Task<VipPlanDto?> UpsertPlanAsync(UpsertVipPlanRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);
        var planId = request.Id.Trim().ToLowerInvariant();
        var existing = await context.VipPlans.FirstOrDefaultAsync(p => p.Id == planId);
        if (existing is not null)
        {
            existing.Name = request.Name.Trim();
            existing.DurationDays = request.DurationDays;
            existing.PriceVnd = request.Price;
            existing.Description = request.Description?.Trim() ?? string.Empty;
            existing.IsActive = request.IsActive;
            await context.SaveChangesAsync();

            var dto = MapPlan(existing);
            if (request.OriginalPrice.HasValue) dto.OriginalPrice = request.OriginalPrice;
            if (!string.IsNullOrWhiteSpace(request.DiscountBadge)) dto.DiscountBadge = request.DiscountBadge;
            if (request.Benefits is { Count: > 0 }) dto.Benefits = request.Benefits;
            dto.IsRecommended = request.IsRecommended;
            return dto;
        }

        var newPlan = new VipPlanEntity
        {
            Id = planId,
            Name = request.Name.Trim(),
            DurationDays = request.DurationDays,
            PriceVnd = request.Price,
            Description = request.Description?.Trim() ?? string.Empty,
            IsActive = request.IsActive,
            DisplayOrder = await context.VipPlans.CountAsync() + 1
        };
        context.VipPlans.Add(newPlan);
        await context.SaveChangesAsync();

        var newDto = MapPlan(newPlan);
        if (request.OriginalPrice.HasValue) newDto.OriginalPrice = request.OriginalPrice;
        if (!string.IsNullOrWhiteSpace(request.DiscountBadge)) newDto.DiscountBadge = request.DiscountBadge;
        if (request.Benefits is { Count: > 0 }) newDto.Benefits = request.Benefits;
        newDto.IsRecommended = request.IsRecommended;
        return newDto;
    }

    public async Task<CouponValidationResultDto> ValidateCouponAsync(ApplyCouponRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);

        var planId = request.PlanId?.Trim().ToLowerInvariant() ?? string.Empty;
        var plan = await context.VipPlans.FirstOrDefaultAsync(candidate => candidate.Id == planId);
        if (plan is null || !plan.IsActive)
        {
            return new CouponValidationResultDto
            {
                IsValid = false,
                Message = "Gói VIP không hợp lệ hoặc đã ngừng hoạt động."
            };
        }

        var code = request.CouponCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var originalPrice = plan.PriceVnd;

        var coupon = await context.Coupons.FirstOrDefaultAsync(c => c.Code == code);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (coupon is null || !coupon.IsActive || coupon.ValidUntilUtc < now)
        {
            return new CouponValidationResultDto
            {
                IsValid = false,
                CouponCode = code,
                OriginalPrice = originalPrice,
                FinalPrice = originalPrice,
                Message = "Mã giảm giá không hợp lệ hoặc đã hết lượt áp dụng."
            };
        }

        decimal discountAmount;
        decimal discountPercent;

        if (coupon.DiscountType.Equals("percent", StringComparison.OrdinalIgnoreCase))
        {
            discountPercent = coupon.DiscountValue;
            discountAmount = originalPrice * (discountPercent / 100m);
        }
        else // fixed amount
        {
            discountPercent = 0;
            discountAmount = Math.Min(originalPrice, coupon.DiscountValue);
        }

        var finalPrice = Math.Max(0, originalPrice - discountAmount);

        var message = coupon.DiscountType.Equals("percent", StringComparison.OrdinalIgnoreCase)
            ? $"Áp dụng thành công mã {coupon.Code}: Giảm {discountPercent:0.#}%{(coupon.Code == "TESTVIP" ? "!" : " tổng đơn hàng!")}"
            : $"Áp dụng thành công mã {coupon.Code}: Giảm {coupon.DiscountValue:N0}đ trực tiếp vào đơn hàng!";

        return new CouponValidationResultDto
        {
            IsValid = true,
            CouponCode = coupon.Code,
            DiscountPercent = discountPercent,
            DiscountAmount = discountAmount,
            OriginalPrice = originalPrice,
            FinalPrice = finalPrice,
            Message = message
        };
    }

    public async Task<PaymentDto?> CreatePaymentAsync(Guid userId, CreatePaymentRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);

        var planId = request.PlanId?.Trim().ToLowerInvariant() ?? string.Empty;
        var plan = await context.VipPlans.FirstOrDefaultAsync(candidate => candidate.Id == planId);
        if (plan is null || !plan.IsActive || !Enum.IsDefined(request.Method))
        {
            return null;
        }

        var amount = plan.PriceVnd;
        string? note = null;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var coupon = await ValidateCouponAsync(new ApplyCouponRequest
            {
                PlanId = plan.Id,
                CouponCode = request.CouponCode
            });

            if (coupon.IsValid)
            {
                amount = coupon.FinalPrice;
                note = $"Áp dụng mã giảm giá {coupon.CouponCode} (-{coupon.DiscountAmount:N0}đ)";
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
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

    public async Task<PaymentWebhookResult> ProcessWebhookAsync(PaymentWebhookRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderCode) || string.IsNullOrWhiteSpace(request.Signature))
        {
            return PaymentWebhookResult.InvalidSig("Dữ liệu webhook không hợp lệ hoặc thiếu thông tin bắt buộc.");
        }

        var verification = _webhookVerifier.Verify(request);
        if (!verification.IsValid)
        {
            return PaymentWebhookResult.InvalidSig(verification.ErrorMessage ?? "Chữ ký xác thực webhook không hợp lệ (403 Forbidden).");
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        await EnsureSeededAsync(context);

        var payment = await context.Payments
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.OrderCode == request.OrderCode.Trim());

        if (payment is null)
        {
            return PaymentWebhookResult.NotFound($"Không tìm thấy đơn hàng với mã '{request.OrderCode}'.");
        }

        if (payment.Status == PaymentStatus.Completed)
        {
            return PaymentWebhookResult.AlreadyCompleted(MapPayment(payment), "Giao dịch đã được xử lý trước đó.");
        }

        if (!string.IsNullOrWhiteSpace(request.TransactionReference))
        {
            var trxUsed = await context.Payments.AnyAsync(p =>
                p.TransactionReference == request.TransactionReference.Trim() &&
                p.Id != payment.Id &&
                p.Status == PaymentStatus.Completed);

            if (trxUsed)
            {
                return PaymentWebhookResult.DuplicateTransaction("Mã giao dịch ngân hàng đã được sử dụng cho đơn hàng khác.");
            }
        }

        if (request.Amount > 0 && Math.Abs(payment.Amount - request.Amount) > 0.01m)
        {
            return PaymentWebhookResult.AmountMismatch("Số tiền thanh toán không khớp với giá trị đơn hàng.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var statusNorm = request.Status.Trim().ToUpperInvariant();
        var isSuccess = statusNorm is "PAID" or "COMPLETED" or "SUCCESS" or "00";

        if (!isSuccess)
        {
            payment.Status = PaymentStatus.Failed;
            payment.Note = string.IsNullOrWhiteSpace(payment.Note)
                ? $"Cổng báo không thành công: {request.Status}"
                : $"{payment.Note} | Trạng thái cổng: {request.Status}";
            await context.SaveChangesAsync();
            return PaymentWebhookResult.GatewayFailed(MapPayment(payment), "Đơn hàng đã được đánh dấu không thành công.");
        }

        payment.Status = PaymentStatus.Completed;
        payment.PaidAtUtc = now;
        payment.TransactionReference = request.TransactionReference?.Trim();

        var plan = await context.VipPlans.FindAsync(payment.PlanId);
        var extendDays = plan?.DurationDays ?? (payment.PlanId switch
        {
            "vip-365" => 365,
            "vip-180" => 180,
            "vip-90" => 90,
            _ => 30
        });

        var user = payment.User;
        NotificationEntity? notification = null;
        if (user is not null)
        {
            user.Membership = MembershipTier.Vip;

            var startsAt = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > now
                ? user.VipExpiresAtUtc.Value
                : now;

            user.VipExpiresAtUtc = startsAt.AddDays(extendDays);
            payment.VipExpiresAtUtc = user.VipExpiresAtUtc;

            notification = new NotificationEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Title = "Nâng cấp VIP thành công",
                Copy = $"Gói {payment.PlanName} của bạn đã được kích hoạt. Hạn dùng đến {user.VipExpiresAtUtc:dd/MM/yyyy}.",
                Icon = "crown",
                ActionUrl = "/vip",
                Read = false,
                CreatedAtUtc = now
            };
            context.Notifications.Add(notification);
        }

        IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync();

            var refreshed = await context.Payments.AsNoTracking().Include(p => p.User)
                .FirstOrDefaultAsync(p => p.OrderCode == request.OrderCode.Trim());
            if (refreshed?.Status == PaymentStatus.Completed)
            {
                return PaymentWebhookResult.AlreadyCompleted(MapPayment(refreshed), "Giao dịch đã được xử lý đồng thời trước đó.");
            }
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        if (hubContext != null && user != null && notification != null)
        {
            try
            {
                var notifDto = new NotificationDto
                {
                    Id = notification.Id,
                    Title = notification.Title,
                    Copy = notification.Copy,
                    CreatedAt = new DateTimeOffset(notification.CreatedAtUtc, TimeSpan.Zero),
                    Time = "Vừa xong",
                    Icon = notification.Icon,
                    Read = false,
                    ActionUrl = notification.ActionUrl
                };
                await hubContext.Clients.Group($"user-{user.Id}").SendAsync("ReceiveNotification", notifDto);
            }
            catch
            {
                // Background broadcast failure must not fail the webhook confirmation
            }
        }

        return PaymentWebhookResult.Ok(MapPayment(payment), "Xác thực thanh toán webhook thành công.");
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
}
