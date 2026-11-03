using AcxiomCRM.Models;
using System.Security.Claims;

namespace AcxiomCRM.Data;

public static class CrmQueryScopes
{
    public static IQueryable<TRecord> AccessibleTo<TRecord>(
        this IQueryable<TRecord> records,
        ClaimsPrincipal principal,
        IQueryable<ApplicationUser> users)
        where TRecord : CrmRecord
    {
        if (principal.IsInRole("Admin")) return records;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.IsInRole("Manager"))
        {
            return records.Where(record =>
                record.OwnerUserId == userId ||
                users.Any(owner => owner.Id == record.OwnerUserId && owner.ManagerUserId == userId));
        }

        return records.Where(record => record.OwnerUserId == userId);
    }
}
