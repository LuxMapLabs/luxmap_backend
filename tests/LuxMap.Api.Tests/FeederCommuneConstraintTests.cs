using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// O-7 — a pole's circuit must live in the pole's own commune, enforced by the DATABASE.
/// </summary>
/// <remarks>
/// <para>
/// The rule itself is not new. <c>RequireFeederInCommuneAsync</c> has enforced it since
/// BE-REVIEW-02 on all three write paths, and it still runs first and still owns the readable 409
/// <c>CROSS_COMMUNE_REFERENCE</c> naming both communes — <c>AssetReplaceAndDeleteTests</c> covers
/// that surface and these tests deliberately do not repeat it.
/// </para>
/// <para>
/// What is new is that forgetting to call it no longer opens the hole. Every test here goes through
/// <c>DbContext</c> with <c>EnterUnscopedSystemWriteBackdoor</c> held open, so the entry-point check
/// is absent, <c>CommuneWriteGuard</c> is bypassed, and the only thing left standing is the
/// composite foreign key. That is the exact position BE-39's seeder and BE-43's sync will write
/// from, and it is why the constraint had to exist before BE-13.
/// </para>
/// <para>
/// ⚠️ These tests would all pass against the OLD single-column foreign key too — except
/// <see cref="The_database_refuses_a_cross_commune_circuit_even_with_every_application_check_bypassed"/>
/// and <see cref="Moving_a_pole_out_of_its_feeders_commune_is_refused_even_though_no_request_can_ask_for_it"/>,
/// which are the two that fail without it. The others exist to prove the constraint refuses the bad
/// rows and not simply every row.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FeederCommuneConstraintTests(AssetImportFixture fixture)
{
    /// <summary>The constraint the database answers with, named so a rename cannot pass unnoticed.</summary>
    private const string CompositeKey = "fk_pole_feeder_feeder_id_commune_id";

    /// <summary>
    /// The whole point of the ticket: a circuit in another commune is refused with every application
    /// layer switched off.
    /// </summary>
    /// <remarks>
    /// Before this constraint the insert below SUCCEEDED. The row was not invisible and threw
    /// nothing — it was a pole in one commune drawing power from a cabinet in another, which is the
    /// state BE-REVIEW-02 constraint 1 was written to prevent and could only prevent by asking every
    /// future write path to remember a function call.
    /// </remarks>
    [Fact]
    public async Task The_database_refuses_a_cross_commune_circuit_even_with_every_application_check_bypassed()
    {
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var segmentId = await NewSegmentAsync(fixture.ForeignCommuneId);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsSystemAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(segmentId, fixture.ForeignCommuneId, feederId));
            return await db.SaveChangesAsync();
        }));

        var failure = Assert.IsType<PostgresException>(thrown.InnerException);

        // 23503 = foreign_key_violation. Matched on SQLSTATE rather than on the message, which is
        // localised, and on the constraint name, which is what DescribeRefusal puts in details.
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        Assert.Equal(CompositeKey, failure.ConstraintName);

        Assert.Equal(0, await CountPolesOnAsync(feederId));
    }

    /// <summary>
    /// The mirror case, and the one that proves the constraint is not simply refusing everything: the
    /// same write with both rows in the SAME commune goes in.
    /// </summary>
    [Fact]
    public async Task A_circuit_inside_the_poles_own_commune_is_accepted()
    {
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        await WriteAsSystemAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(segmentId, fixture.CommuneId, feederId));
            return await db.SaveChangesAsync();
        });

        Assert.Equal(1, await CountPolesOnAsync(feederId));
    }

    /// <summary>
    /// 🔴 A pole with NO feeder stays legal, although its <c>commune_id</c> is non-null.
    /// </summary>
    /// <remarks>
    /// This is <c>MATCH SIMPLE</c>, the PostgreSQL default, and it is the reason the composite key
    /// could be added at all: under MATCH SIMPLE a row where ANY foreign-key column is null skips the
    /// check entirely. Under <c>MATCH FULL</c> the same row would be rejected because it demands the
    /// columns be null together — and since <c>commune_id</c> is never null, MATCH FULL would reject
    /// EVERY feeder-less pole in the table. A <c>solar_all_in_one</c> pole sits on no circuit at all,
    /// so that is most of them.
    /// <para>
    /// Kept as its own test because "tighten it to MATCH FULL" reads like a correctness improvement
    /// in review, and nothing else in the suite would go red.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_pole_with_no_feeder_is_still_legal_even_though_its_commune_is_not_null()
    {
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var poleId = await WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(segmentId, fixture.CommuneId, feederId: null);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        Assert.Equal(1, await CountAsync(pole => pole.PoleId == poleId && pole.FeederId == null));
    }

    /// <summary>
    /// Clearing a pole's circuit stays legal — the sharp edge of <c>PUT</c> still works.
    /// </summary>
    /// <remarks>
    /// A full-replacement <c>PUT</c> with no <c>feeder_id</c> clears the circuit by design
    /// (<c>Replacing_a_pole_without_a_feeder_id_clears_its_circuit</c>). That is an UPDATE setting one
    /// half of a composite foreign key to null, which is worth pinning separately from the INSERT
    /// case: MATCH SIMPLE applies to both, but only a test says so.
    /// </remarks>
    [Fact]
    public async Task Clearing_a_poles_circuit_stays_legal_under_the_composite_key()
    {
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var poleId = await WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(segmentId, fixture.CommuneId, feederId);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        await WriteAsSystemAsync(async db =>
        {
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
            pole.FeederId = null;
            return await db.SaveChangesAsync();
        });

        Assert.Equal(0, await CountPolesOnAsync(feederId));
    }

    /// <summary>
    /// 🔴 The case the application check NEVER covered: moving the POLE instead of the feeder.
    /// </summary>
    /// <remarks>
    /// <c>RequireFeederInCommuneAsync</c> runs when a feeder is assigned, and reads the pole's commune
    /// as the fixed side of the comparison. Nothing in it fires when the pole's OWN commune changes
    /// underneath an existing circuit. No HTTP request can ask for that today — <c>commune_id</c> is
    /// absent from all three update bodies precisely because a transfer is not an edit — but "no
    /// endpoint offers it" is a property of this week's controllers, not of the data.
    /// <para>
    /// This is the half of O-7 that was never reachable from the application layer at all, which is
    /// the clearest argument for why the rule belonged in the schema.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Moving_a_pole_out_of_its_feeders_commune_is_refused_even_though_no_request_can_ask_for_it()
    {
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var poleId = await WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(segmentId, fixture.CommuneId, feederId);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsSystemAsync(async db =>
        {
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
            pole.CommuneId = fixture.ForeignCommuneId;
            return await db.SaveChangesAsync();
        }));

        Assert.Equal(CompositeKey, Assert.IsType<PostgresException>(thrown.InnerException).ConstraintName);

        // Still where it was, and still wired to the same cabinet.
        Assert.Equal(1, await CountAsync(pole =>
            pole.PoleId == poleId && pole.CommuneId == fixture.CommuneId && pole.FeederId == feederId));
    }

    /// <summary>
    /// A pole MAY still sit on a segment owned by another commune. The asymmetry is the design.
    /// </summary>
    /// <remarks>
    /// <c>road_class = inter_commune</c> means the road runs BETWEEN communes, so a pole in a
    /// different commune from its segment's owner is correct data (BE-REVIEW-02, constraint 1). Only
    /// the electrical circuit has to match. Asserted at the DATABASE level rather than over HTTP —
    /// where <c>AssetReplaceAndDeleteTests</c> already covers it — because the risk this one guards
    /// is somebody adding a composite key to <c>segment_id</c> "for symmetry" with the one O-7 just
    /// added, which would silently make legitimate inter-commune roads unwritable.
    /// </remarks>
    [Fact]
    public async Task A_pole_may_still_sit_on_a_segment_owned_by_another_commune()
    {
        var segmentId = await NewSegmentAsync(fixture.ForeignCommuneId);

        var poleId = await WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(segmentId, fixture.CommuneId, feederId: null);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        Assert.Equal(1, await CountAsync(pole => pole.PoleId == poleId && pole.SegmentId == segmentId));
    }

    /// <summary>
    /// The alternate key on <c>feeder</c> is still declared, and the foreign key still spans two
    /// columns.
    /// </summary>
    /// <remarks>
    /// A model-level assertion, not a behavioural one, and it earns its place because the alternate
    /// key looks like dead weight: <c>feeder_id</c> is already the primary key, so uniqueness over
    /// <c>(feeder_id, commune_id)</c> adds nothing on its own. It is there only because PostgreSQL
    /// will not point a foreign key at columns without a unique constraint. Deleting it as redundant
    /// takes the composite key with it, and the behavioural tests above would then fail with a
    /// message about a missing constraint rather than about the rule that was lost.
    /// </remarks>
    [Fact]
    public async Task The_composite_key_and_the_alternate_key_it_needs_are_both_declared()
    {
        var (foreignKeyColumns, alternateKeyColumns) = await fixture.QueryAsync(db =>
        {
            var pole = db.Model.FindEntityType(typeof(Pole))!;

            var composite = pole.GetForeignKeys()
                .Single(fk => fk.PrincipalEntityType.ClrType == typeof(Feeder))
                .Properties.Select(p => p.Name).ToList();

            var alternate = db.Model.FindEntityType(typeof(Feeder))!
                .GetKeys()
                .Where(key => !key.IsPrimaryKey())
                .Select(key => key.Properties.Select(p => p.Name).ToList())
                .Single();

            return Task.FromResult((composite, alternate));
        });

        Assert.Equal([nameof(Pole.FeederId), nameof(Pole.CommuneId)], foreignKeyColumns);
        Assert.Equal([nameof(Feeder.FeederId), nameof(Feeder.CommuneId)], alternateKeyColumns);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────
    //
    // Everything is written through the backdoor on purpose: these tests are about what survives
    // when the application layer is not there, so building the fixtures through the API would put
    // the very checks under test back in the way.

    private async Task<T> WriteAsSystemAsync<T>(Func<LuxMapDbContext, Task<T>> write)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            return await write(db);
        }
    }

    private Task<string> NewFeederAsync(string communeId) => WriteAsSystemAsync(async db =>
    {
        var feeder = new Feeder { FeederName = "O-7 probe cabinet", CommuneId = communeId };
        db.Set<Feeder>().Add(feeder);
        await db.SaveChangesAsync();
        return feeder.FeederId;
    });

    private Task<string> NewSegmentAsync(string communeId) => WriteAsSystemAsync(async db =>
    {
        var segment = new RoadSegment
        {
            SegmentName = "O-7 probe road",
            RoadClass = RoadClass.InterCommune,
            LengthM = 100,
            Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
            CommuneId = communeId,
            DataSource = DataSource.PublicImagery,
        };

        db.Set<RoadSegment>().Add(segment);
        await db.SaveChangesAsync();
        return segment.SegmentId;
    });

    private static Pole NewPole(string segmentId, string communeId, string? feederId) => new()
    {
        SegmentId = segmentId,
        CommuneId = communeId,
        FeederId = feederId,
        Geom = new Point(106.49, 10.97) { SRID = 4326 },
        DataSource = DataSource.PublicImagery,
    };

    private Task<int> CountPolesOnAsync(string feederId)
        => CountAsync(pole => pole.FeederId == feederId);

    private Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Pole, bool>> predicate)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().CountAsync(predicate));
}
