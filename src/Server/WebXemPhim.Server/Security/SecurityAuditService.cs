using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Server.Security;

public interface ISecurityAuditService
{
    Task RecordAsync(Guid? userId, string eventType, bool success, string detail = "");
}

public sealed class SecurityAuditService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ISecurityAuditService
{
    public async Task RecordAsync(Guid? userId, string eventType, bool success, string detail = "")
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SecurityAuditEvents.Add(new SecurityAuditEventEntity
        {
            Id = Guid.NewGuid(), UserId = userId, EventType = eventType[..Math.Min(eventType.Length, 80)],
            Success = success, Detail = detail[..Math.Min(detail.Length, 240)], CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync();
    }
}
