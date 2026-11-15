namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// The rule behind "a pole carries ONE fitting in service" (ONE-FIXTURE, drift OF-1), in ONE place.
/// </summary>
/// <remarks>
/// MULTI-FIXTURE (11/11/2026) allowed two fittings per pole, one per slot; ONE-FIXTURE (15/11/2026) went back to one:
/// a pole has one fitting, its lamps are identical (<see cref="Fixture.LampCount"/>), and a failure replaces the whole
/// fitting. The slot column stays so the published read shapes (<c>fixtures[]</c>, <c>fixture_count</c>) keep working
/// and the limit can be widened again without a schema change.
/// <para>
/// The limit is enforced by the database — unique <c>(pole_id, slot) WHERE removed_date IS NULL</c> plus
/// <c>ck_fixture_active_slot</c> (a fitting in service sits in slot 1) — so it holds however the row arrives.
/// Services only pick a free slot so they can answer 409 with a message before the index has to.
/// </para>
/// </remarks>
public static class FixtureSlots
{
    public const short First = 1;

    public const short MaxInService = 1;

    /// <summary>
    /// The widest slot a row may RECORD (<c>ck_fixture_slot</c>): slot 2 is legal only on a retired fitting, as
    /// history from the MULTI-FIXTURE window. A fitting in service is held to <see cref="MaxInService"/>.
    /// </summary>
    public const short MaxRecorded = 2;

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
    /// With one fitting in service (ONE-FIXTURE) the newest is the only one; the ordering stays so the anchor is
    /// still well defined if <see cref="MaxInService"/> is ever widened. Replacing the fitting inserts a newer row,
    /// so the anchor changes and the old baseline stops matching. The MF-5 gap (retiring the older of two lamps
    /// without a replacement left a stale baseline in force) cannot arise while a pole holds one fitting.
    /// </remarks>
    public static IOrderedQueryable<Fixture> NewestFirst(this IQueryable<Fixture> fixtures)
        => fixtures.OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.FixtureId.Length)
            .ThenByDescending(f => f.FixtureId);
}
