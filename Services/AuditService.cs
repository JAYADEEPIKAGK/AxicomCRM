using AcxiomCRM.Data;
using AcxiomCRM.Models;
using System.Security.Claims;

namespace AcxiomCRM.Services;

public class AuditService(CrmDbContext db, IHttpContextAccessor accessor) : IAuditService
{
    public async Task RecordAsync(string action, string? entityType = null, string? entityId = null, string? details = null, string? actorUserId = null, string? actorUserName = null)
    {
        var user = accessor.HttpContext?.User;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId ?? user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = Truncate(actorUserName ?? user?.Identity?.Name, 160),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync();
    }

    private static string? Truncate(string? value, int length)
        => value is { Length: > 0 } && value.Length > length ? value[..length] : value;
}
