namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The rule behind "a pole carries at most two lamps in service" (MULTI-FIXTURE, drift MF-1), in ONE place.
/// </summary>
/// <remarks>
/// The limit is enforced by the database — unique <c>(pole_id, slot) WHERE removed_date IS NULL</c> plus
/// <c>CHECK (slot BETWEEN 1 AND 2)</c> — so it holds however the row arrives. Services only pick a free slot
/// so they can answer 409 with a message before the index has to.
/// </remarks>
public static class FixtureSlots
{
    public const short First = 1;

    public const short MaxInService = 2;

    /// <summary>The lowest slot not in <paramref name="inUse"/>, or <c>null</c> when every slot is taken.</summary>
    public static short? FirstFree(IEnumerable<short> inUse)
    {
        var taken = inUse.ToHashSet();
        for (var slot = First; slot <= MaxInService; slot++)
        {
            if (!taken.Contains(slot))
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// The one fitting a pole's luminance baseline is keyed to: the newest in service, by the repo's ID order
    /// (<c>created_at, length(id), id</c> — never the bare ID, <c>FIX-10000 &lt; FIX-9999</c>).
    /// </summary>
    /// <remarks>
    /// Replacing EITHER lamp inserts a newer row, so the anchor changes and the old baseline stops matching —
    /// the same "a baseline built on a lamp that has since been replaced is not the pole's baseline" rule the
    /// single-lamp model had. Known gap: retiring the older of two lamps WITHOUT a replacement leaves the newer
    /// one as anchor, so the baseline stays in force although the pole now gives less light — the pole may read
    /// <c>dim</c> until a fresh baseline accumulates (drift MF-5).
    /// </remarks>
    public static IOrderedQueryable<Fixture> NewestFirst(this IQueryable<Fixture> fixtures)
        => fixtures.OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.FixtureId.Length)
            .ThenByDescending(f => f.FixtureId);
}
