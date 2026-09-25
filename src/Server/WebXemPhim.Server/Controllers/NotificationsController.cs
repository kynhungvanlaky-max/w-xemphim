using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> GetNotifications()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var now = UtcNow;
        var list = await context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        var dtos = list.Select(n => new NotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Copy = n.Copy,
            CreatedAt = new DateTimeOffset(n.CreatedAtUtc, TimeSpan.Zero),
            Time = FormatTime(n.CreatedAtUtc, now),
            Icon = n.Icon,
            Read = n.Read,
            ActionUrl = n.ActionUrl
        }).ToList();

        return Ok(ApiResponse<List<NotificationDto>>.Ok(dtos));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<bool>>> MarkRead(Guid id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var item = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId);
        if (item is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy thông báo."));
        }

        item.Read = true;
        await context.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("read-all")]
    public async Task<ActionResult<ApiResponse<bool>>> MarkAllRead()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var unread = await context.Notifications
            .Where(n => n.UserId == UserId && !n.Read)
            .ToListAsync();

        foreach (var item in unread)
        {
            item.Read = true;
        }

        await context.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private static string FormatTime(DateTime utcTime, DateTime nowUtc)
    {
        var diff = nowUtc - utcTime;
        if (diff.TotalMinutes < 1) return "Vừa xong";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} phút";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} giờ";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} ngày trước";
        return utcTime.ToLocalTime().ToString("dd/MM/yyyy");
    }
}
