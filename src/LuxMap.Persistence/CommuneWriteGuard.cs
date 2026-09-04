using System.Net;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LuxMap.Persistence;

/// <summary>
/// Contract section 7 applied to WRITES. The counterpart to the query filter, which only ever
/// covered reads.
/// </summary>
/// <remarks>
/// <b>Why a second mechanism was needed at all.</b> <c>HasQueryFilter</c> puts <c>commune_id</c> into
/// the <c>WHERE</c> clause of a query. It has nothing to do with <c>Add</c>, <c>Update</c> or
/// <c>Remove</c>, so until this guard existed a caller scoped to one commune could write a row
/// belonging to another. The foreign key does not help: it proves the commune EXISTS, never that the
/// caller may write to it. The row would then be hidden from its own author by the very filter that
/// was supposed to be protecting it — no exception, no log line, data that simply vanishes.
/// <para>
/// <b>Why it lives in <c>SaveChanges</c> rather than in an action filter.</b> Same reasoning as
/// <c>ValidateCommuneReferences</c>: an attribute has to be remembered on every new controller, and
/// the one that gets forgotten is the one that leaks. Everything reaches the database through
/// <c>SaveChanges</c>, so a check here covers BE-12 today and the entities of BE-15, BE-18 and BE-21
/// before they are written.
/// </para>
/// </remarks>
internal static class CommuneWriteGuard
{
    /// <summary>
    /// Throws unless every tracked <see cref="ICommuneScoped"/> write stays inside
    /// <paramref name="scope"/>.
    /// </summary>
    /// <remarks>
    /// Called BEFORE <c>base.SaveChanges</c>, deliberately. Raising it from a save interceptor or
    /// from inside the transaction would let EF wrap it in a <c>DbUpdateException</c>, and the BE-04
    /// middleware would then report 500 <c>INTERNAL_ERROR</c> instead of the 403 this is.
    /// </remarks>
    public static void Enforce(ChangeTracker changeTracker, CommuneScope scope)
    {
        // Administrators carry '*'. Nothing else is treated as unrestricted — in particular an EMPTY
        // scope is a refusal, not a pass. Empty is exactly what an unauthenticated caller and a token
        // with no commune_ids both produce, and both must be denied.
        if (scope.IsSystemWide)
        {
            return;
        }

        List<string>? violations = null;

        foreach (var entry in changeTracker.Entries<ICommuneScoped>())
        {
            foreach (var communeId in CommunesTouchedBy(entry))
            {
                if (!scope.Allows(communeId))
                {
                    violations ??= [];
                    violations.Add($"{entry.Entity.GetType().Name} → {communeId}");
                }
            }
        }

        if (violations is null)
        {
            return;
        }

        throw new LuxMapException(
            ErrorCodes.CommuneForbidden,
            HttpStatusCode.Forbidden,
            "This write touches a commune outside your permitted scope.",
            new Dictionary<string, object?>
            {
                // The caller supplied these values, so echoing them reveals nothing it does not
                // already know — the same reasoning CommuneFilter.Narrow uses.
                ["rejected_writes"] = violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            });
    }

    /// <summary>
    /// Which commune(s) a single change touches.
    /// </summary>
    /// <remarks>
    /// <b>Modified is checked on BOTH sides.</b> Moving a row from a commune inside the scope to one
    /// outside gives away an asset; moving one from outside to inside takes an asset that was never
    /// the caller's to touch. Both are the same privilege violation wearing different clothes, and
    /// checking only <c>CurrentValues</c> would catch just one of them.
    /// <para>
    /// <b>Deleted is checked too</b>, on the ORIGINAL value — the row as it exists in the database.
    /// Deleting another commune's asset is the most destructive form of the violation this guard
    /// exists for, and leaving it out would mean a caller cannot create a foreign row but can erase
    /// one. Note that cascades performed BY THE DATABASE are invisible here, which is a real limit:
    /// <c>fixture</c> and <c>pole_current_status</c> cascade from <c>pole</c>, so a permitted pole
    /// deletion still removes its children without them appearing in the change tracker.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> CommunesTouchedBy(EntityEntry<ICommuneScoped> entry)
    {
        const string Property = nameof(ICommuneScoped.CommuneId);

        switch (entry.State)
        {
            case EntityState.Added:
                yield return entry.Entity.CommuneId;
                break;

            case EntityState.Modified:
                yield return entry.Entity.CommuneId;

                if (entry.OriginalValues[Property] is string original
                    && !string.Equals(original, entry.Entity.CommuneId, StringComparison.Ordinal))
                {
                    yield return original;
                }

                break;

            case EntityState.Deleted:
                if (entry.OriginalValues[Property] is string removed)
                {
                    yield return removed;
                }

                break;
        }
    }
}
