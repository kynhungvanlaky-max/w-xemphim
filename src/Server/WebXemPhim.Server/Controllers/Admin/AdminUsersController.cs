using WebXemPhim.Server.Services;
using Microsoft.Extensions.Caching.Memory;
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
    IHttpClientFactory? httpClientFactory = null,
    IStreamSessionService? streamSessionService = null,
    IMemoryCache? memoryCache = null,
    IAuthSessionCache? authSessionCache = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private readonly IAuthSessionCache? _sessionCache = authSessionCache;
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers(
        [FromQuery] string? q = null,
        [FromQuery] MembershipTier? tier = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        [FromQuery] string? lockStatus = null,
        [FromQuery] string? vipStatus = null,
        [FromQuery] string? registeredPeriod = null,
        [FromQuery] string? sortBy = null)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Users.AsQueryable();

        if (tier.HasValue)
        {
            query = query.Where(u => u.Membership == tier.Value);
        }

        var utcNow = UtcNow;
        var roleFilter = role?.Trim().ToLower();
        var lockFilter = lockStatus?.Trim().ToLower();
        var vipFilter = vipStatus?.Trim().ToLower();

        if (roleFilter is "admin" or "user")
        {
            var securityQuery = context.Set<UserSecurityEntity>().AsQueryable();
            securityQuery = roleFilter == "admin"
                ? securityQuery.Where(s => s.IsAdmin)
                : securityQuery.Where(s => !s.IsAdmin);
            var matchingIds = await securityQuery.Select(s => s.UserId).ToListAsync();
            query = query.Where(u => matchingIds.Contains(u.Id));
        }

        if (lockFilter is "locked" or "unlocked" or "active")
        {
            var lockedIds = await context.Set<UserSecurityEntity>()
                .Where(s => s.LockedUntilUtc.HasValue && s.LockedUntilUtc.Value > utcNow)
                .Select(s => s.UserId)
                .ToListAsync();
            query = lockFilter == "locked"
                ? query.Where(u => lockedIds.Contains(u.Id))
                : query.Where(u => !lockedIds.Contains(u.Id));
        }

        if (vipFilter == "active")
        {
            query = query.Where(u => u.Membership != MembershipTier.Free && u.VipExpiresAtUtc.HasValue && u.VipExpiresAtUtc.Value > utcNow);
        }
        else if (vipFilter == "expiring_soon")
        {
            var deadline = utcNow.AddDays(7);
            query = query.Where(u => u.Membership != MembershipTier.Free
                && u.VipExpiresAtUtc.HasValue
                && u.VipExpiresAtUtc.Value > utcNow
                && u.VipExpiresAtUtc.Value <= deadline);
        }
        else if (vipFilter == "expired")
        {
            query = query.Where(u => u.Membership != MembershipTier.Free && (!u.VipExpiresAtUtc.HasValue || u.VipExpiresAtUtc.Value <= utcNow));
        }
        else if (vipFilter is "none" or "never")
        {
            query = query.Where(u => u.Membership == MembershipTier.Free);
        }

        var periodFilter = registeredPeriod?.Trim().ToLower();
        if (periodFilter == "7d")
        {
            var from = utcNow.AddDays(-7);
            query = query.Where(u => u.CreatedAtUtc >= from);
        }
        else if (periodFilter == "30d")
        {
            var from = utcNow.AddDays(-30);
            query = query.Where(u => u.CreatedAtUtc >= from);
        }
        else if (periodFilter == "this_year")
        {
            var year = utcNow.Year;
            var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddYears(1);
            query = query.Where(u => u.CreatedAtUtc >= start && u.CreatedAtUtc < end);
        }
        else if (periodFilter == "older")
        {
            var year = utcNow.Year;
            var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            query = query.Where(u => u.CreatedAtUtc < start);
        }

        var pagination = Pagination.Normalize(page, pageSize);
        page = pagination.Page;
        pageSize = pagination.PageSize;

        var sortKey = sortBy?.ToLowerInvariant();
        var isRelational = context.Database.IsRelational();
        var useSqlSearch = isRelational && !string.IsNullOrWhiteSpace(q);
        var useSqlNameSort = isRelational && sortKey is "name_asc" or "name_desc";

        if (useSqlSearch)
        {
            // Trên PostgreSQL, FullName/Email bị mã hoá nên tìm qua bản lower-case không mã hoá
            // (SearchNameLower/NormalizedEmail) — 1 query có index thay vì nạp toàn bộ bảng.
            var s = q!.Trim().ToLower();
            query = query.Where(u => EF.Functions.Like(u.SearchNameLower, $"%{s}%")
                                  || EF.Functions.Like(u.NormalizedEmail, $"%{s}%"));
        }

        if (useSqlNameSort)
        {
            query = sortKey == "name_desc"
                ? query.OrderByDescending(u => u.SearchNameLower)
                : query.OrderBy(u => u.SearchNameLower);
        }

        var needsInMemoryDecrypt = !isRelational && (!string.IsNullOrWhiteSpace(q) || sortKey is "name_asc" or "name_desc");

        int totalItems;
        List<UserEntity> rawUsers;

        if (needsInMemoryDecrypt)
        {
            var materialized = await query.ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var s = q.Trim();
                materialized = materialized.Where(u =>
                    (!string.IsNullOrEmpty(u.Email) && u.Email.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(u.NormalizedEmail) && u.NormalizedEmail.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(u.FullName) && u.FullName.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            IEnumerable<UserEntity> sorted = sortKey switch
            {
                "name_asc" => materialized.OrderBy(u => u.FullName, StringComparer.CurrentCultureIgnoreCase),
                "name_desc" => materialized.OrderByDescending(u => u.FullName, StringComparer.CurrentCultureIgnoreCase),
                "oldest" or "date_asc" => materialized.OrderBy(u => u.CreatedAtUtc),
                "vip_desc" => materialized
                    .OrderByDescending(u => u.VipExpiresAtUtc.HasValue)
                    .ThenByDescending(u => u.VipExpiresAtUtc),
                _ => materialized.OrderByDescending(u => u.CreatedAtUtc)
            };

            totalItems = materialized.Count;
            rawUsers = sorted.Skip(pagination.Skip).Take(pageSize).ToList();
        }
        else
        {
            if (!useSqlNameSort)
            {
                query = sortKey switch
                {
                    "oldest" or "date_asc" => query.OrderBy(u => u.CreatedAtUtc),
                    "vip_desc" => query
                        .OrderByDescending(u => u.VipExpiresAtUtc.HasValue)
                        .ThenByDescending(u => u.VipExpiresAtUtc),
                    _ => query.OrderByDescending(u => u.CreatedAtUtc)
                };
            }

            totalItems = await query.CountAsync();
            rawUsers = await query
                .Skip(pagination.Skip)
                .Take(pageSize)
                .ToListAsync();
        }

        var userIds = rawUsers.Select(u => u.Id).ToList();
        var securities = await context.Set<UserSecurityEntity>()
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId);

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

    [HttpPost("bulk-action")]
    public async Task<ActionResult<ApiResponse<int>>> BulkUserAction([FromBody] AdminBulkUserActionRequest request)
    {
        if (request.UserIds.Count == 0)
        {
            return BadRequest(ApiResponse<int>.Fail("Vui lòng chọn ít nhất một người dùng."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var ids = request.UserIds.Distinct().ToList();
        var users = await context.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        if (users.Count == 0)
        {
            return Ok(ApiResponse<int>.Ok(0, "Không tìm thấy người dùng nào."));
        }

        var action = request.Action.Trim().ToLowerInvariant();
        var securities = await context.Set<UserSecurityEntity>()
            .Where(s => ids.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId);
        var now = UtcNow;
        var count = 0;

        // Gom token/MFA cần thu hồi vào 1 query thay vì N query trong vòng lặp.
        var tokensToRevoke = action == "lock"
            ? await context.AccessTokens.Where(t => ids.Contains(t.UserId)).ToListAsync()
            : new List<AccessTokenEntity>();
        var mfaToRevoke = action == "lock"
            ? await context.MfaVerifiedSessions.Where(t => ids.Contains(t.UserId)).ToListAsync()
            : new List<MfaVerifiedSessionEntity>();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            switch (action)
            {
                case "lock":
                    context.AccessTokens.RemoveRange(tokensToRevoke);
                    context.MfaVerifiedSessions.RemoveRange(mfaToRevoke);
                    foreach (var user in users)
                    {
                        if (CallerUserId.HasValue && CallerUserId.Value == user.Id) continue;
                        if (!securities.TryGetValue(user.Id, out var sec))
                        {
                            sec = new UserSecurityEntity { UserId = user.Id, PasswordIterations = 100000 };
                            context.Set<UserSecurityEntity>().Add(sec);
                        }
                        sec.LockedUntilUtc = now.AddMinutes(Math.Max(15, request.LockMinutes));
                        sec.FailedLoginAttempts = 0;
                        count++;
                    }
                    break;

                case "unlock":
                    foreach (var user in users)
                    {
                        if (!securities.TryGetValue(user.Id, out var sec))
                        {
                            sec = new UserSecurityEntity { UserId = user.Id, PasswordIterations = 100000 };
                            context.Set<UserSecurityEntity>().Add(sec);
                        }
                        sec.LockedUntilUtc = null;
                        sec.FailedLoginAttempts = 0;
                        count++;
                    }
                    break;

                case "grant_vip":
                    foreach (var user in users)
                    {
                        user.Membership = MembershipTier.Vip;
                        var baseDate = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > now
                            ? user.VipExpiresAtUtc.Value
                            : now;
                        user.VipExpiresAtUtc = baseDate.AddDays(Math.Max(1, request.VipDays));
                        count++;
                    }
                    break;

                case "revoke_vip":
                    foreach (var user in users)
                    {
                        user.Membership = MembershipTier.Free;
                        user.VipExpiresAtUtc = null;
                        count++;
                    }
                    break;

                default:
                    return BadRequest(ApiResponse<int>.Fail($"Hành động không hợp lệ: {request.Action}"));
            }

            await context.SaveChangesAsync();
            await LogAuditAsync(context, $"BULK_USER_{action.ToUpperInvariant()}", "User", string.Join(",", ids), $"Thao tác '{action}' trên {count} người dùng.");
            if (tx != null) await tx.CommitAsync();

            foreach (var id in ids)
            {
                _sessionCache?.InvalidateUser(id);
            }

            if (action is "lock" or "revoke_vip" && streamSessionService is not null)
            {
                foreach (var id in ids)
                {
                    try { await streamSessionService.RevokeUserStreamsAsync(id); }
                    catch { /* best-effort */ }
                }
            }
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

        return Ok(ApiResponse<int>.Ok(count, $"Đã hoàn tất thao tác '{action}' trên {count} người dùng."));
    }

    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetUserDetail(Guid id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<AdminUserDetailDto>.Fail("Không tìm thấy người dùng."));

        var userFullName = user.FullName;
        var userEmail = user.Email;

        // Chạy song song các truy vấn độc lập với DbContext riêng (DbContext không thread-safe).
        var securityTask = LoadSecurityAsync(id);
        var watchlistTask = CountWatchlistAsync(id);
        var reviewsCountTask = CountReviewsAsync(id);
        var recentPaymentsTask = LoadRecentPaymentsAsync(id);
        var recentReviewsTask = LoadRecentReviewsAsync(id, userFullName, userEmail);

        await Task.WhenAll(securityTask, watchlistTask, reviewsCountTask, recentPaymentsTask, recentReviewsTask);

        var security = await securityTask;
        var watchlistCount = await watchlistTask;
        var reviewsCount = await reviewsCountTask;
        var recentPayments = await recentPaymentsTask;
        var recentReviews = await recentReviewsTask;

        async Task<UserSecurityEntity?> LoadSecurityAsync(Guid userId)
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Set<UserSecurityEntity>().AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId);
        }

        async Task<int> CountWatchlistAsync(Guid userId)
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.UserLibrary.CountAsync(ul => ul.UserId == userId);
        }

        async Task<int> CountReviewsAsync(Guid userId)
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Reviews.CountAsync(r => r.UserId == userId);
        }

        async Task<List<PaymentDto>> LoadRecentPaymentsAsync(Guid userId)
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Payments
                .AsNoTracking()
                .Where(p => p.UserId == userId)
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
        }

        async Task<List<AdminReviewDto>> LoadRecentReviewsAsync(Guid userId, string fullName, string email)
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Reviews
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(5)
                .Select(r => new AdminReviewDto
                {
                    Id = r.Id,
                    MovieId = r.MovieId,
                    MovieTitle = r.Movie.Title,
                    MoviePosterUrl = r.Movie.PosterUrl,
                    UserId = r.UserId,
                    UserFullName = fullName,
                    UserEmail = email,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                    AdminReply = r.AdminReply,
                    AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                    IsReported = r.IsReported
                })
                .ToListAsync();
        }

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
        else if (!user.VipExpiresAtUtc.HasValue || user.VipExpiresAtUtc.Value <= UtcNow)
        {
            // VipExpiresAtUtc = null được toàn bộ hệ thống hiểu là VIP vĩnh viễn. Không được
            // để thao tác "đổi hạng" vô tình tạo ra đặc quyền vĩnh viễn khi không nhập ngày.
            return BadRequest(ApiResponse<UserDto>.Fail(
                "Cần nhập số ngày gia hạn để cấp gói thành viên khi tài khoản chưa có hạn VIP còn hiệu lực."));
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
            memoryCache?.Remove($"vip_status_{id}");
            memoryCache?.Remove($"isvip:{id}");
            if (request.MembershipTier == MembershipTier.Free && streamSessionService is not null)
            {
                await streamSessionService.RevokeUserStreamsAsync(id);
            }
            _sessionCache?.InvalidateUser(id);
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

        _sessionCache?.InvalidateUser(id);

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
            if (request.IsLocked)
            {
                memoryCache?.Remove($"vip_status_{id}");
                memoryCache?.Remove($"isvip:{id}");
                if (streamSessionService is not null)
                {
                    await streamSessionService.RevokeUserStreamsAsync(id);
                }
            }
            _sessionCache?.InvalidateUser(id);
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
            user = await context.Users.FirstOrDefaultAsync(u => u.EmailLookupHash == emailHash || u.NormalizedEmail == termLower || u.NormalizedEmail == termUpper);
        }

        if (user is null)
        {
            if (context.Database.IsRelational())
            {
                // FullName/Email bị mã hoá nên dùng bản lower-case không mã hoá để tìm.
                var termNormalized = rawTerm.ToLowerInvariant();
                user = await context.Users.FirstOrDefaultAsync(u =>
                    u.NormalizedEmail == termNormalized || u.SearchNameLower == termNormalized);
            }
            else
            {
                var allUsers = await context.Users.ToListAsync();
                user = allUsers.FirstOrDefault(u =>
                    string.Equals(u.Email, rawTerm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.NormalizedEmail, rawTerm, StringComparison.OrdinalIgnoreCase));

                if (user is null)
                {
                    var matchingUsers = allUsers
                        .Where(u => string.Equals(u.FullName, rawTerm, StringComparison.OrdinalIgnoreCase))
                        .Take(2)
                        .ToList();
                    if (matchingUsers.Count > 1)
                    {
                        return BadRequest(ApiResponse<UserDto>.Fail($"Có nhiều người dùng cùng tên '{rawTerm}'. Vui lòng nhập chính xác địa chỉ Email để cấp quyền VIP."));
                    }
                    user = matchingUsers.FirstOrDefault();
                }
            }
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
            _sessionCache?.InvalidateUser(user.Id);
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
