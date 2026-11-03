namespace AcxiomCRM.Services;

public interface IAuditService
{
    Task RecordAsync(string action, string? entityType = null, string? entityId = null, string? details = null, string? actorUserId = null, string? actorUserName = null);
}
