using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin")]
public sealed class AdminOverviewController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null,
    ILogger<AdminOverviewController>? logger = null,
    IMemoryCache? memoryCache = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private const string StatsCacheKey = "admin:stats:v1";
    private static readonly TimeSpan StatsCacheTtl = TimeSpan.FromSeconds(30);
    private readonly IMemoryCache? _cache = memoryCache;
    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> GetStats()
    {
        if (_cache is not null && _cache.TryGetValue(StatsCacheKey, out AdminDashboardDto? cached) && cached is not null)
        {
            return Ok(ApiResponse<AdminDashboardDto>.Ok(cached));
        }

        var now = UtcNow;

        // Mỗi nhóm truy vấn dùng DbContext riêng (DbContext không thread-safe) và chạy song song,
        // tránh việc cửa admin phải chờ hàng chục round-trip tuần tự.
        var catalogTask = LoadCatalogCountsAsync();
        var userCountsTask = LoadUserCountsAsync();
        var paymentCountsTask = LoadPaymentCountsAsync();
        var reviewCountsTask = LoadReviewCountsAsync();
        var recentUsersTask = LoadRecentUsersAsync();
        var recentPaymentsTask = LoadRecentPaymentsAsync();
        var recentReviewsTask = LoadRecentReviewsAsync();
        var genreDistributionTask = LoadGenreDistributionAsync();

        await Task.WhenAll(
            catalogTask, userCountsTask, paymentCountsTask, reviewCountsTask,
            recentUsersTask, recentPaymentsTask, recentReviewsTask, genreDistributionTask);

        var catalog = await catalogTask;
        var userCounts = await userCountsTask;
        var paymentCounts = await paymentCountsTask;
        var reviewCounts = await reviewCountsTask;

        var dto = new AdminDashboardDto
        {
            TotalMovies = catalog.Movies,
            TotalSeries = catalog.Series,
            TotalAnime = catalog.Anime,
            TotalEpisodes = catalog.Episodes,
            TotalUsers = userCounts.Total,
            ActiveVipUsers = userCounts.ActiveVip,
            FreeUsers = userCounts.Total - userCounts.ActiveVip,
            VipMembers = userCounts.VipMembers,
            AdminUsers = userCounts.Admins,
            LockedUsers = userCounts.Locked,
            ExpiringSoonVipUsers = userCounts.ExpiringSoon,
            TotalRevenue = paymentCounts.Revenue,
            TotalTransactions = paymentCounts.Transactions,
            TotalReviews = reviewCounts.Total,
            AverageRating = Math.Round(reviewCounts.AverageRating, 1),
            RecentUsers = await recentUsersTask,
            RecentPayments = await recentPaymentsTask,
            RecentReviews = await recentReviewsTask,
            GenreDistribution = await genreDistributionTask
        };

        _cache?.Set(StatsCacheKey, dto, StatsCacheTtl);
        return Ok(ApiResponse<AdminDashboardDto>.Ok(dto));

        async Task<(int Movies, int Series, int Anime, int Episodes)> LoadCatalogCountsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            var movies = await c.Movies.CountAsync(m => m.Type == MovieType.Movie);
            var series = await c.Movies.CountAsync(m => m.Type == MovieType.Series);
            var anime = await c.Movies.CountAsync(m => m.Type == MovieType.Anime);
            var episodes = await c.Episodes.CountAsync();
            return (movies, series, anime, episodes);
        }

        async Task<(int Total, int ActiveVip, int VipMembers, int Admins, int Locked, int ExpiringSoon)> LoadUserCountsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            var total = await c.Users.CountAsync();
            var activeVip = await c.Users.CountAsync(u => u.Membership != MembershipTier.Free && u.VipExpiresAtUtc > now);
            var vipMembers = await c.Users.CountAsync(u => u.Membership != MembershipTier.Free);
            var admins = await c.Set<UserSecurityEntity>().CountAsync(s => s.IsAdmin);
            var locked = await c.Set<UserSecurityEntity>().CountAsync(s => s.LockedUntilUtc.HasValue && s.LockedUntilUtc.Value > now);
            var expiringSoon = await c.Users.CountAsync(u =>
                u.Membership != MembershipTier.Free
                && u.VipExpiresAtUtc.HasValue
                && u.VipExpiresAtUtc.Value > now
                && u.VipExpiresAtUtc.Value <= now.AddDays(7));
            return (total, activeVip, vipMembers, admins, locked, expiringSoon);
        }

        async Task<(decimal Revenue, int Transactions)> LoadPaymentCountsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            var revenue = await c.Payments
                .Where(p => p.Status == PaymentStatus.Completed)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
            var transactions = await c.Payments.CountAsync();
            return (revenue, transactions);
        }

        async Task<(int Total, double AverageRating)> LoadReviewCountsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            var total = await c.Reviews.CountAsync();
            var average = total > 0 ? await c.Reviews.AverageAsync(r => (double)r.Rating) : 0.0;
            return (total, average);
        }

        async Task<List<UserDto>> LoadRecentUsersAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Users
                .AsNoTracking()
                .OrderByDescending(u => u.CreatedAtUtc)
                .Take(5)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    MembershipTier = u.Membership,
                    SubscriptionExpiresAt = u.VipExpiresAtUtc.HasValue ? new DateTimeOffset(u.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                    CreatedAt = new DateTimeOffset(u.CreatedAtUtc, TimeSpan.Zero),
                    Initials = GetInitials(u.FullName)
                })
                .ToListAsync();
        }

        async Task<List<PaymentDto>> LoadRecentPaymentsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Payments
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(5)
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

        async Task<List<AdminReviewDto>> LoadRecentReviewsAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.Reviews
                .AsNoTracking()
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(5)
                .Select(r => new AdminReviewDto
                {
                    Id = r.Id,
                    MovieId = r.MovieId,
                    MovieTitle = r.Movie.Title,
                    MoviePosterUrl = r.Movie.PosterUrl,
                    UserId = r.UserId,
                    UserFullName = r.User.FullName,
                    UserEmail = r.User.Email,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                    AdminReply = r.AdminReply,
                    AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                    IsReported = r.IsReported
                })
                .ToListAsync();
        }

        async Task<Dictionary<string, int>> LoadGenreDistributionAsync()
        {
            await using var c = await ContextFactory.CreateDbContextAsync();
            return await c.MovieGenres
                .AsNoTracking()
                .GroupBy(mg => mg.Genre.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Name, g => g.Count);
        }
    }

    [HttpGet("system/health")]
    public async Task<ActionResult<ApiResponse<SystemHealthDto>>> GetSystemHealth()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var totalMovies = await context.Movies.CountAsync();
        var totalEpisodes = await context.Episodes.CountAsync();
        var totalUsers = await context.Users.CountAsync();
        var nowUtc = UtcNow;
        var activeStreamsCount = await context.ActiveStreams.CountAsync(s => s.IsActive && s.ExpiresAtUtc > nowUtc);

        long dbSizeBytes = 0;
        try
        {
            if (context.Database.IsNpgsql())
            {
                // PostgreSQL reports the on-disk size of the active database.
                dbSizeBytes = await context.Database
                    .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
                    .SingleAsync();
            }
            else
            {
                var dbPath = "webxemphim.db";
                if (System.IO.File.Exists(dbPath))
                {
                    dbSizeBytes = new System.IO.FileInfo(dbPath).Length;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Không thể đọc kích thước cơ sở dữ liệu.");
        }

        var memMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2);
        string uptimeStr = "0h 0m";
        try
        {
            var startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            var diff = DateTime.Now - startTime;
            uptimeStr = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Không thể xác định thời gian uptime của tiến trình máy chủ.");
        }

        var dto = new SystemHealthDto
        {
            DbSizeBytes = dbSizeBytes,
            DbSizeFormatted = dbSizeBytes > 0 ? $"{Math.Round(dbSizeBytes / (1024.0 * 1024.0), 2)} MB" : "—",
            ServerUptime = uptimeStr,
            TotalMovies = totalMovies,
            TotalEpisodes = totalEpisodes,
            TotalUsers = totalUsers,
            ActiveStreamsCount = activeStreamsCount,
            MemoryUsageMb = memMb,
            Environment = Env?.EnvironmentName ?? "Development",
            ServerTimeUtc = UtcNow
        };

        return Ok(ApiResponse<SystemHealthDto>.Ok(dto));
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> GetAuditLogs(
        [FromQuery] string? q = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int take = 0)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(a => a.Summary.ToLower().Contains(s) || a.AdminEmail.ToLower().Contains(s) || a.EntityId.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType.ToLower() == entityType.Trim().ToLower());
        }

        var requestedPageSize = take > 0 ? take : pageSize;
        var pagination = Pagination.Normalize(page, requestedPageSize, defaultPageSize: 50, maxPageSize: 200);
        page = pagination.Page;
        var effectivePageSize = pagination.PageSize;

        var totalItems = await query.CountAsync();
        var logs = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip(pagination.Skip)
            .Take(effectivePageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                Timestamp = new DateTimeOffset(a.TimestampUtc, TimeSpan.Zero),
                AdminEmail = a.AdminEmail,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Summary = a.Summary,
                DetailsJson = a.DetailsJson
            })
            .ToListAsync();

        var pagedResult = new PagedResult<AuditLogDto>
        {
            Items = logs,
            TotalItems = totalItems,
            Page = page,
            PageSize = effectivePageSize
        };

        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(pagedResult));
    }
}