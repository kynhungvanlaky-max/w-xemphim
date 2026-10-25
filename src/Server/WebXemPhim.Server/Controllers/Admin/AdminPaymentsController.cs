using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin")]
public sealed class AdminPaymentsController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null,
    IHubContext<CinemaHub>? hubContext = null,
    IMemoryCache? memoryCache = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedResult<PaymentDto>>>> GetPayments(
        [FromQuery] string? q = null,
        [FromQuery] PaymentStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? method = null,
        [FromQuery] string? sortBy = null)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Payments.Include(p => p.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(p => p.OrderCode.ToLower().Contains(s)
                                  || p.PlanName.ToLower().Contains(s)
                                  || (p.User != null && (p.User.Email.ToLower().Contains(s) || p.User.FullName.ToLower().Contains(s))));
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var methodFilter = method?.Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(methodFilter))
        {
            if (methodFilter == "vietqr" || methodFilter == "banktransfer")
            {
                query = query.Where(p => p.Method == PaymentMethod.BankTransfer);
            }
            else if (methodFilter == "momo")
            {
                query = query.Where(p => p.Method == PaymentMethod.Momo);
            }
            else if (methodFilter == "visa")
            {
                query = query.Where(p => p.Method == PaymentMethod.Visa);
            }
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "amount_asc" => query.OrderBy(p => p.Amount),
            "amount_desc" => query.OrderByDescending(p => p.Amount),
            "oldest" => query.OrderBy(p => p.CreatedAtUtc),
            _ => query.OrderByDescending(p => p.CreatedAtUtc)
        };

        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 1000) pageSize = 1000;
        var totalItems = await query.CountAsync();
        var payments = await query
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                UserId = p.UserId,
                UserEmail = p.User != null ? p.User.Email : null,
                UserFullName = p.User != null ? p.User.FullName : null,
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                CreatedAt = new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
                PaidAt = p.PaidAtUtc.HasValue ? new DateTimeOffset(p.PaidAtUtc.Value, TimeSpan.Zero) : null,
                VipExpiresAt = p.VipExpiresAtUtc.HasValue ? new DateTimeOffset(p.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                TransactionReference = p.TransactionReference,
                Note = p.Note
            })
            .ToListAsync();

        var pagedResult = new PagedResult<PaymentDto>
        {
            Items = payments,
            TotalItems = totalItems,
            Page = Math.Max(page, 1),
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<PaymentDto>>.Ok(pagedResult));
    }

    [HttpPost("payments/{id:guid}/mark-paid")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> MarkPaymentPaid(Guid id, [FromBody] ManualPaymentApprovalRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var payment = await context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound(ApiResponse<PaymentDto>.Fail("Không tìm thấy đơn thanh toán."));

        if (payment.Status == PaymentStatus.Completed)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã được thanh toán trước đó."));
        }

        payment.Status = PaymentStatus.Completed;
        payment.PaidAtUtc = UtcNow;
        payment.TransactionReference = request.TransactionReference.Trim();
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            payment.Note = request.Note.Trim();
        }

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
            var currentExpiry = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > UtcNow
                ? user.VipExpiresAtUtc.Value
                : UtcNow;
            user.VipExpiresAtUtc = currentExpiry.AddDays(extendDays);
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
                CreatedAtUtc = UtcNow
            };
            context.Notifications.Add(notification);
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            memoryCache?.Remove($"vip_status_{payment.UserId}");
            memoryCache?.Remove($"isvip:{payment.UserId}");
            var auditDetails = System.Text.Json.JsonSerializer.Serialize(new
            {
                AdminEmail = CallerAdminEmail,
                payment.OrderCode,
                payment.UserId,
                payment.Amount,
                payment.PlanId,
                TransactionReference = payment.TransactionReference,
                OldStatus = PaymentStatus.Pending.ToString(),
                NewStatus = PaymentStatus.Completed.ToString()
            });
            await LogAuditAsync(context, "APPROVE_PAYMENT", "Payment", payment.OrderCode, $"Duyệt thanh toán thủ công cho đơn '{payment.OrderCode}', mã GD: {request.TransactionReference}.", auditDetails);
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync();
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã được xử lý hoặc thanh toán trước đó."));
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        if (hubContext != null && notification != null && user != null)
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
                // Background broadcast failure should not abort response
            }
        }

        var dto = new PaymentDto
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
            CreatedAt = new DateTimeOffset(payment.CreatedAtUtc, TimeSpan.Zero),
            PaidAt = payment.PaidAtUtc.HasValue ? new DateTimeOffset(payment.PaidAtUtc.Value, TimeSpan.Zero) : null,
            VipExpiresAt = payment.VipExpiresAtUtc.HasValue ? new DateTimeOffset(payment.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            TransactionReference = payment.TransactionReference,
            Note = payment.Note
        };

        return Ok(ApiResponse<PaymentDto>.Ok(dto, "Đã xác nhận thanh toán và gia hạn VIP cho người dùng thành công."));
    }

    [HttpPost("payments/{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> RejectPayment(Guid id, [FromBody] PaymentRejectionRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var payment = await context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound(ApiResponse<PaymentDto>.Fail("Không tìm thấy đơn thanh toán."));

        if (payment.Status == PaymentStatus.Completed)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã hoàn tất, không thể từ chối."));
        }
        if (payment.Status == PaymentStatus.Failed)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã bị từ chối trước đó."));
        }

        payment.Status = PaymentStatus.Failed;
        payment.Note = string.IsNullOrWhiteSpace(payment.Note)
            ? $"Từ chối duyệt: {request.Reason.Trim()}"
            : $"{payment.Note} | Từ chối: {request.Reason.Trim()}";

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            var auditDetails = System.Text.Json.JsonSerializer.Serialize(new
            {
                AdminEmail = CallerAdminEmail,
                payment.OrderCode,
                payment.UserId,
                payment.Amount,
                payment.PlanId,
                Reason = request.Reason.Trim(),
                OldStatus = PaymentStatus.Pending.ToString(),
                NewStatus = PaymentStatus.Failed.ToString()
            });
            await LogAuditAsync(context, "REJECT_PAYMENT", "Payment", payment.OrderCode, $"Từ chối duyệt đơn '{payment.OrderCode}', lý do: {request.Reason}.", auditDetails);
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        var dto = new PaymentDto
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
            CreatedAt = new DateTimeOffset(payment.CreatedAtUtc, TimeSpan.Zero),
            PaidAt = payment.PaidAtUtc.HasValue ? new DateTimeOffset(payment.PaidAtUtc.Value, TimeSpan.Zero) : null,
            VipExpiresAt = payment.VipExpiresAtUtc.HasValue ? new DateTimeOffset(payment.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            TransactionReference = payment.TransactionReference,
            Note = payment.Note
        };

        return Ok(ApiResponse<PaymentDto>.Ok(dto, "Đã từ chối đơn thanh toán thành công."));
    }

    [HttpGet("vip-plans")]
    public async Task<ActionResult<ApiResponse<List<VipPlanDto>>>> GetVipPlans([FromServices] IPaymentService paymentSvc)
    {
        var plans = await paymentSvc.GetAdminPlansAsync();
        return Ok(ApiResponse<List<VipPlanDto>>.Ok(plans));
    }

    [HttpPut("vip-plans/{id}")]
    public async Task<ActionResult<ApiResponse<VipPlanDto>>> UpsertVipPlan(string id, [FromBody] UpsertVipPlanRequest request, [FromServices] IPaymentService paymentSvc)
    {
        request.Id = id;
        var updated = await paymentSvc.UpsertPlanAsync(request);
        if (updated is null) return BadRequest(ApiResponse<VipPlanDto>.Fail("Không thể cập nhật gói VIP."));

        await using var context = await ContextFactory.CreateDbContextAsync();
        await LogAuditAsync(context, "UPDATE_VIP_PLAN", "VipPlan", id, $"Cập nhật cấu hình gói VIP '{updated.Name}' ({updated.Price:N0}đ, {updated.DurationDays} ngày).");

        return Ok(ApiResponse<VipPlanDto>.Ok(updated, $"Đã lưu cấu hình gói '{updated.Name}' thành công."));
    }
}
