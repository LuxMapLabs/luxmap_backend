using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// The one place an asset row is stamped with who last changed it and when (drift POLE-NOTE N-4).
/// </summary>
internal static class AssetStamp
{
    /// <summary>
    /// Stamps <paramref name="asset"/> after the caller has assigned its new values — and only if a value
    /// really changed. Call it after <c>Add</c> for a new row.
    /// </summary>
    /// <returns><c>true</c> for a new or changed row; <c>false</c> when every assignment matched what was stored.</returns>
    /// <remarks>
    /// <c>Entry()</c> runs change detection for this one entity, so "changed" is EF's own comparison of the
    /// stored snapshot with the current values. That is what lets a re-imported identical file — or a form
    /// saved without edits — leave <c>updated_by</c> and <c>updated_at</c> alone: assigning a value equal to
    /// the stored one never marks a property modified. Nothing else may set <c>UpdatedAt</c> on these four
    /// tables; an explicit assignment would itself count as the change.
    /// </remarks>
    public static bool Touch(LuxMapDbContext dbContext, IUpdateStamped asset, ICurrentActorAccessor actor)
    {
        switch (dbContext.Entry(asset).State)
        {
            case EntityState.Added:
                asset.UpdatedBy = actor.UserId;
                return true;
            case EntityState.Modified:
                asset.UpdatedBy = actor.UserId;
                asset.UpdatedAt = UtcMicrosecondClock.UtcNow();
                return true;
            default:
                return false;
        }
    }
}
