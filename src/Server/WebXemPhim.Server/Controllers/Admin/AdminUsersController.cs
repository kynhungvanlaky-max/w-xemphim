using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/users")]
public sealed class AdminUsersController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers(
        [FromQuery] string? q = null,
        [FromQuery] MembershipTier? tier = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(s) || u.FullName.ToLower().Contains(s));
        }

        if (tier.HasValue)
        {
            query = query.Where(u => u.Membership == tier.Value);
        }

        if (pageSize <= 0) pageSize = 20;
        var totalItems = await query.CountAsync();
        var rawUsers = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userIds = rawUsers.Select(u => u.Id).ToList();
        var securities = await context.Set<UserSecurityEntity>()
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId);

        var utcNow = UtcNow;
        var users = rawUsers.Select(u =>
        {
            securities.TryGetValue(u.Id, out var sec);
            var isLocked = sec?.LockedUntilUtc.HasValue == true && sec.LockedUntilUtc.Value > utcNow;
            return new UserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                MembershipTier = u.Membership,
                SubscriptionExpiresAt = u.VipExpiresAtUtc.HasValue ? new DateTimeOffset(u.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                CreatedAt = new DateTimeOffset(u.CreatedAtUtc, TimeSpan.Zero),
                Initials = GetInitials(u.FullName),
                IsAdmin = sec?.IsAdmin ?? false,
                IsLocked = isLocked,
                LockedUntil = sec?.LockedUntilUtc.HasValue == true ? new DateTimeOffset(sec.LockedUntilUtc.Value, TimeSpan.Zero) : null
            };
        }).ToList();

        var pagedResult = new PagedResult<UserDto>
        {
            Items = users,
            TotalItems = totalItems,
            Page = Math.Max(page, 1),
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(pagedResult));
    }

    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetUserDetail(Guid id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<AdminUserDetailDto>.Fail("Không tìm thấy người dùng."));

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        var watchlistCount = await context.UserLibrary.CountAsync(ul => ul.UserId == id);
        var reviewsCount = await context.Reviews.CountAsync(r => r.UserId == id);

        var recentPayments = await context.Payments
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(10)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                UserId = p.UserId,
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                CreatedAt = new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
                PaidAt = p.PaidAtUtc.HasValue ? new DateTimeOffset(p.PaidAtUtc.Value, TimeSpan.Zero) : null,
                VipExpiresAt = p.VipExpiresAtUtc.HasValue ? new DateTimeOffset(p.VipExpiresAtUtc.Value, TimeSpan.Zero) : null
            })
            .ToListAsync();

        var recentReviews = await context.Reviews
            .Where(r => r.UserId == id)
            .Include(r => r.Movie)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(5)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                MovieId = r.MovieId,
                MovieTitle = r.Movie.Title,
                MoviePosterUrl = r.Movie.PosterUrl,
                UserId = r.UserId,
                UserFullName = user.FullName,
                UserEmail = user.Email,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                AdminReply = r.AdminReply,
                AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                IsReported = r.IsReported
            })
            .ToListAsync();

        var isLocked = security?.LockedUntilUtc.HasValue == true && security.LockedUntilUtc.Value > UtcNow;

        var dto = new AdminUserDetailDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName),
            IsAdmin = security?.IsAdmin ?? false,
            IsLocked = isLocked,
            LockedUntil = security?.LockedUntilUtc.HasValue == true ? new DateTimeOffset(security.LockedUntilUtc.Value, TimeSpan.Zero) : null,
            FailedLoginAttempts = security?.FailedLoginAttempts ?? 0,
            WatchlistCount = watchlistCount,
            ReviewsCount = reviewsCount,
            RecentPayments = recentPayments,
            RecentReviews = recentReviews
        };

        return Ok(ApiResponse<AdminUserDetailDto>.Ok(dto));
    }

    [HttpPut("{id:guid}/membership")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUserMembership(
        Guid id,
        [FromBody] AdminUpdateUserRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound(ApiResponse<UserDto>.Fail("Không tìm thấy người dùng."));
        }

        user.Membership = request.MembershipTier;
        if (request.MembershipTier == MembershipTier.Free)
        {
            user.VipExpiresAtUtc = null;
        }
        else if (request.ExtendDays > 0)
        {
            var baseDate = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > UtcNow
                ? user.VipExpiresAtUtc.Value
                : UtcNow;
            user.VipExpiresAtUtc = baseDate.AddDays(request.ExtendDays);
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "UPDATE_MEMBERSHIP", "User", id.ToString(), $"Cập nhật hạng thành viên của '{user.Email}' thành {request.MembershipTier}.");
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

        var sec = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        var isLocked = sec?.LockedUntilUtc.HasValue == true && sec.LockedUntilUtc.Value > UtcNow;

        var dto = new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName),
            IsAdmin = sec?.IsAdmin ?? false,
            IsLocked = isLocked,
            LockedUntil = sec?.LockedUntilUtc.HasValue == true ? new DateTimeOffset(sec.LockedUntilUtc.Value, TimeSpan.Zero) : null
        };

        return Ok(ApiResponse<UserDto>.Ok(dto, "Cập nhật hạng thành viên thành công."));
    }

    [HttpPut("{id:guid}/admin-role")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserAdminRole(Guid id, [FromBody] AdminUpdateUserRoleRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy người dùng."));

        if (CallerUserId.HasValue && CallerUserId.Value == id && !request.IsAdmin)
        {
            return BadRequest(ApiResponse<bool>.Fail("Bạn không thể tự hạ quyền quản trị của chính mình."));
        }

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        if (security is null)
        {
            security = new UserSecurityEntity
            {
                UserId = id,
                IsAdmin = request.IsAdmin,
                PasswordIterations = 100000,
                FailedLoginAttempts = 0
            };
            context.Set<UserSecurityEntity>().Add(security);
        }
        else
        {
            if (security.IsAdmin && !request.IsAdmin)
            {
                var activeAdminCount = await context.Set<UserSecurityEntity>()
                    .CountAsync(s => s.IsAdmin && (s.LockedUntilUtc == null || s.LockedUntilUtc <= UtcNow));
                if (activeAdminCount <= 1)
                {
                    return BadRequest(ApiResponse<bool>.Fail("Không thể hạ quyền quản trị viên duy nhất còn lại trong hệ thống."));
                }

                var tokens = await context.AccessTokens.Where(t => t.UserId == id).ToListAsync();
                context.AccessTokens.RemoveRange(tokens);
                var mfaSessions = await context.MfaVerifiedSessions.Where(t => t.UserId == id).ToListAsync();
                context.MfaVerifiedSessions.RemoveRange(mfaSessions);
            }
            security.IsAdmin = request.IsAdmin;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "UPDATE_ADMIN_ROLE", "User", id.ToString(), $"Đổi quyền Quản trị của '{user.Email}' thành: {request.IsAdmin}.");
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

        return Ok(ApiResponse<bool>.Ok(security.IsAdmin, $"Đã cập nhật quyền Quản trị: {(security.IsAdmin ? "Bật" : "Tắt")}."));
    }

    [HttpPut("{id:guid}/lock-status")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserLockStatus(Guid id, [FromBody] AdminUpdateUserLockRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy người dùng."));

        if (CallerUserId.HasValue && CallerUserId.Value == id && request.IsLocked)
        {
            return BadRequest(ApiResponse<bool>.Fail("Bạn không thể tự khóa tài khoản quản trị của chính mình."));
        }

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        if (security is null)
        {
            security = new UserSecurityEntity
            {
                UserId = id,
                IsAdmin = false,
                PasswordIterations = 100000,
                FailedLoginAttempts = 0
            };
            context.Set<UserSecurityEntity>().Add(security);
        }

        if (request.IsLocked)
        {
            if (security.IsAdmin)
            {
                var otherActiveAdminCount = await context.Set<UserSecurityEntity>()
                    .CountAsync(s => s.IsAdmin && s.UserId != id && (s.LockedUntilUtc == null || s.LockedUntilUtc <= UtcNow));
                if (otherActiveAdminCount == 0)
                {
                    return BadRequest(ApiResponse<bool>.Fail("Không thể khóa quản trị viên duy nhất còn lại trong hệ thống."));
                }
            }

            security.LockedUntilUtc = UtcNow.AddMinutes(Math.Max(15, request.LockMinutes));

            var tokens = await context.AccessTokens.Where(t => t.UserId == id).ToListAsync();
            context.AccessTokens.RemoveRange(tokens);
            var mfaSessions = await context.MfaVerifiedSessions.Where(t => t.UserId == id).ToListAsync();
            context.MfaVerifiedSessions.RemoveRange(mfaSessions);
        }
        else
        {
            security.LockedUntilUtc = null;
            security.FailedLoginAttempts = 0;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            var isLockedNow = security.LockedUntilUtc.HasValue && security.LockedUntilUtc.Value > UtcNow;
            await LogAuditAsync(context, "UPDATE_LOCK_STATUS", "User", id.ToString(), $"Cập nhật trạng thái khóa của '{user.Email}': {(isLockedNow ? "Khóa" : "Mở khóa")}.");
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

        var isLockedResult = security.LockedUntilUtc.HasValue && security.LockedUntilUtc.Value > UtcNow;
        return Ok(ApiResponse<bool>.Ok(isLockedResult, $"Đã cập nhật trạng thái tài khoản: {(isLockedResult ? "Đã khóa" : "Hoạt động")}."));
    }

    [HttpPost("grant-vip")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GrantVipToUser([FromBody] ManualVipGrantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserEmailOrUsername))
        {
            return BadRequest(ApiResponse<UserDto>.Fail("Vui lòng nhập Email hoặc Họ tên người dùng."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var rawTerm = request.UserEmailOrUsername.Trim();
        var termLower = rawTerm.ToLower();
        var termUpper = rawTerm.ToUpper();

        UserEntity? user = null;
        if (Guid.TryParse(rawTerm, out var parsedGuid))
        {
            user = await context.Users.FirstOrDefaultAsync(u => u.Id == parsedGuid);
        }

        if (user is null)
        {
            var emailHash = FieldEncryptor.Current.ComputeLookupHash(rawTerm);
            user = await context.Users.FirstOrDefaultAsync(u => u.EmailLookupHash == emailHash || u.NormalizedEmail == termUpper || u.Email.ToLower() == termLower);
        }

        if (user is null)
        {
            var matchingUsers = await context.Users.Where(u => u.FullName.ToLower() == termLower).Take(2).ToListAsync();
            if (matchingUsers.Count > 1)
            {
                return BadRequest(ApiResponse<UserDto>.Fail($"Có nhiều người dùng cùng tên '{rawTerm}'. Vui lòng nhập chính xác địa chỉ Email để cấp quyền VIP."));
            }
            user = matchingUsers.FirstOrDefault();
        }

        if (user is null)
        {
            return NotFound(ApiResponse<UserDto>.Fail($"Không tìm thấy người dùng có thông tin '{rawTerm}'."));
        }

        var now = UtcNow;
        var currentExpiry = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > now
            ? user.VipExpiresAtUtc.Value
            : now;

        user.Membership = MembershipTier.Vip;
        user.VipExpiresAtUtc = currentExpiry.AddDays(request.Days);

        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderCode = $"GRANT{Guid.NewGuid():N}"[..26],
            UserId = user.Id,
            PlanId = $"manual-{request.Days}d",
            PlanName = $"Đặc cách VIP ({request.Days} ngày)",
            Amount = 0,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Completed,
            CreatedAtUtc = now,
            PaidAtUtc = now,
            VipExpiresAtUtc = user.VipExpiresAtUtc,
            TransactionReference = "ADMIN-MANUAL-GRANT",
            Note = string.IsNullOrWhiteSpace(request.Reason) ? "Đặc cách cấp VIP trực tiếp từ quản trị viên" : $"Đặc cách: {request.Reason.Trim()}"
        };
        context.Payments.Add(payment);
        context.Notifications.Add(new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Cấp quyền VIP thành công",
            Copy = $"Tài khoản của bạn đã được quản trị viên cấp {request.Days} ngày VIP.",
            Icon = "Crown",
            ActionUrl = "/vip",
            CreatedAtUtc = now
        });

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "GRANT_VIP", "User", user.Id.ToString(), $"Cấp {request.Days} ngày VIP cho '{user.Email}'. Lý do: {request.Reason}.");
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

        var userDto = new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName)
        };

        return Ok(ApiResponse<UserDto>.Ok(userDto, $"Đã cấp thành công {request.Days} ngày VIP cho thành viên '{user.FullName}' ({user.Email})."));
    }
}
