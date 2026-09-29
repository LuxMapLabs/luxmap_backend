using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Faults;

/// <summary>Row locks that serialise fault review against work-order creation (BE-19).</summary>
/// <remarks>
/// <para>
/// Why: creating a work order READS a fault's status and inserts a link without writing the fault,
/// so the fault's <c>xmin</c> never changes and optimistic concurrency cannot see the race. Without
/// a lock, a Manager could reject a fault while an inspection is being created over it, or review a
/// fault while a repair is being created over it — both commit, and the rule each checked is broken.
/// </para>
/// <para>
/// Both sides take <c>FOR UPDATE</c> on the fault rows inside their transaction and only THEN read
/// the state they validate. Locks are taken in <c>fault_id</c> order so two callers locking
/// overlapping sets cannot deadlock — this is a lock order, not a display order.
/// </para>
/// </remarks>
public static class FaultLocks
{
    public static async Task LockAsync(LuxMapDbContext db, IReadOnlyCollection<string> faultIds, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Fault row locks are only held inside a transaction.");
        }

        if (faultIds.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM fault WHERE fault_id = ANY({0}) ORDER BY fault_id FOR UPDATE",
            [faultIds.ToArray()], ct);
    }
}
