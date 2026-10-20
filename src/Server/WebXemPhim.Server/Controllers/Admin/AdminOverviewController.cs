using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    ILogger<AdminOverviewController>? logger = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> GetStats()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var now = UtcNow;

        var totalMovies = await context.Movies.CountAsync(m => m.Type == MovieType.Movie);
        var totalSeries = await context.Movies.CountAsync(m => m.Type == MovieType.Series);
        var totalAnime = await context.Movies.CountAsync(m => m.Type == MovieType.Anime);
        var totalEpisodes = await context.Episodes.CountAsync();

        var totalUsers = await context.Users.CountAsync();
        var activeVipUsers = await context.Users.CountAsync(u => u.Membership != MembershipTier.Free && u.VipExpiresAtUtc > now);
        var freeUsers = totalUsers - activeVipUsers;

        var totalRevenue = await context.Payments
            .Where(p => p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var totalTransactions = await context.Payments.CountAsync();

        var totalReviews = await context.Reviews.CountAsync();
        var averageRating = totalReviews > 0
            ? await context.Reviews.AverageAsync(r => (double)r.Rating)
            : 0.0;

        var recentUsers = await context.Users
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

        var recentPayments = await context.Payments
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

        var recentReviews = await context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
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

        var genreDistribution = await context.MovieGenres
            .Include(mg => mg.Genre)
            .GroupBy(mg => mg.Genre.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Name, g => g.Count);

        var dto = new AdminDashboardDto
        {
            TotalMovies = totalMovies,
            TotalSeries = totalSeries,
            TotalAnime = totalAnime,
            TotalEpisodes = totalEpisodes,
            TotalUsers = totalUsers,
            ActiveVipUsers = activeVipUsers,
            FreeUsers = freeUsers,
            TotalRevenue = totalRevenue,
            TotalTransactions = totalTransactions,
            TotalReviews = totalReviews,
            AverageRating = Math.Round(averageRating, 1),
            RecentUsers = recentUsers,
            RecentPayments = recentPayments,
            RecentReviews = recentReviews,
            GenreDistribution = genreDistribution
        };

        return Ok(ApiResponse<AdminDashboardDto>.Ok(dto));
    }

    [HttpGet("system/health")]
    public async Task<ActionResult<ApiResponse<SystemHealthDto>>> GetSystemHealth()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var totalMovies = await context.Movies.CountAsync();
        var totalEpisodes = await context.Episodes.CountAsync();
        var totalUsers = await context.Users.CountAsync();
        var activeStreamsCount = await context.StreamSources.CountAsync();

        long dbSizeBytes = 0;
        try
        {
            var dbPath = "webxemphim.db";
            if (System.IO.File.Exists(dbPath))
            {
                dbSizeBytes = new System.IO.FileInfo(dbPath).Length;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Không thể đọc kích thước tập tin cơ sở dữ liệu webxemphim.db.");
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
            DbSizeFormatted = dbSizeBytes > 0 ? $"{Math.Round(dbSizeBytes / (1024.0 * 1024.0), 2)} MB" : "0.4 MB (SQLite)",
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

        var effectivePageSize = take > 0 ? take : pageSize;
        if (effectivePageSize <= 0) effectivePageSize = 50;
        effectivePageSize = Math.Min(effectivePageSize, 200);

        var totalItems = await query.CountAsync();
        var logs = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((Math.Max(page, 1) - 1) * effectivePageSize)
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
            Page = Math.Max(page, 1),
            PageSize = effectivePageSize
        };

        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(pagedResult));
    }
}
