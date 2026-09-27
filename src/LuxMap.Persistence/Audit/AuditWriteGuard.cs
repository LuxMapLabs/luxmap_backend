using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LuxMap.Persistence.Audit;

internal static class AuditWriteGuard
{
    public static void Enforce(ChangeTracker tracker, bool systemWrite)
    {
        var auditEntries = tracker.Entries<AuditEvent>().ToArray();
        if (auditEntries.Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit events cannot be updated or deleted.");
        }

        if (!systemWrite
            && tracker.Entries<IAudited>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && auditEntries.Count(entry => entry.State == EntityState.Added) != 1)
        {
            throw new InvalidOperationException("An audited operation requires exactly one new audit event.");
        }
    }
}
