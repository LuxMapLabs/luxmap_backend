using System.Diagnostics;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// What <c>CommuneWriteGuard</c> costs on a change tracker the size of a CSV import (BE-12a, C8).
/// </summary>
/// <remarks>
/// The ~3.7 µs per entity recorded in CLAUDE.md was measured on a handful of entities. It leaves out
/// the term that actually grows: <c>ChangeTracker.Entries&lt;T&gt;()</c> calls <c>DetectChanges</c>
/// — the framework documentation says so outright — so the guard forces an EXTRA full snapshot
/// comparison immediately before <c>SaveChanges</c> runs its own. That is O(entities × properties),
/// not O(entities), and an import is where it first matters.
/// <para>
/// Assertions here are deliberately loose. This is a measurement that prints numbers, not a
/// performance gate: a tight threshold on a shared development machine fails for reasons that have
/// nothing to do with the code.
/// </para>
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class CommuneWriteGuardCostTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    private const int TrackedEntities = 1000;

    [Fact]
    public async Task The_guard_walks_a_thousand_tracked_entities_in_a_time_worth_writing_down()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        var segmentId = await SeedSegmentAsync();

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            for (var i = 0; i < TrackedEntities; i++)
            {
                db.Set<Pole>().Add(new Pole
                {
                    ExternalRef = $"COST-{Guid.NewGuid():N}",
                    SegmentId = segmentId,
                    CommuneId = fixture.CommuneId,
                    Geom = new Point(106.49 + (i * 0.0001), 10.97) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                });
            }
        }

        var tracked = db.ChangeTracker.Entries<ICommuneScoped>().Count();
        Assert.Equal(TrackedEntities, tracked);

        // (1) The first Entries<T>() call after the Adds pays for DetectChanges over the whole graph.
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        var cold = Stopwatch.StartNew();
        var entries = db.ChangeTracker.Entries<ICommuneScoped>().ToList();
        cold.Stop();

        // (2) A second call with nothing changed in between: the traversal without a real diff to do.
        var warm = Stopwatch.StartNew();
        _ = db.ChangeTracker.Entries<ICommuneScoped>().ToList();
        warm.Stop();

        // (3) The guard's own work: one scope check per entity, which is what the 3.7 µs figure was.
        var communeScope = CommuneScope.ForCommunes([fixture.CommuneId]);
        var checks = Stopwatch.StartNew();
        var allowed = entries.Count(entry => communeScope.Allows(entry.Entity.CommuneId));
        checks.Stop();

        Assert.Equal(TrackedEntities, allowed);

        output.WriteLine($"tracked entities                : {TrackedEntities}");
        output.WriteLine($"(1) Entries<T>() incl. DetectChanges : {cold.Elapsed.TotalMilliseconds:F2} ms "
            + $"({cold.Elapsed.TotalMicroseconds / TrackedEntities:F2} us/entity)");
        output.WriteLine($"(2) Entries<T>() warm                : {warm.Elapsed.TotalMilliseconds:F2} ms "
            + $"({warm.Elapsed.TotalMicroseconds / TrackedEntities:F2} us/entity)");
        output.WriteLine($"(3) scope check per entity           : {checks.Elapsed.TotalMilliseconds:F2} ms "
            + $"({checks.Elapsed.TotalMicroseconds / TrackedEntities:F2} us/entity)");
        output.WriteLine($"guard total (1 + 3)                  : "
            + $"{(cold.Elapsed + checks.Elapsed).TotalMilliseconds:F2} ms");
        output.WriteLine(
            "DetectChanges is the term that grows; the scope check is noise beside it. EF runs "
            + "DetectChanges again inside SaveChanges, so the guard's real cost is one EXTRA pass.");

        // Nothing is committed: the tracker is dropped without ever reaching the database.
        db.ChangeTracker.Clear();

        Assert.True(
            (cold.Elapsed + checks.Elapsed).TotalMilliseconds < 500,
            "The guard should stay far under half a second at 1000 entities; investigate if it does not.");
    }

    private async Task<string> SeedSegmentAsync()
        => await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    ExternalRef = $"COST-SEG-{Guid.NewGuid():N}",
                    SegmentName = "guard cost probe",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
                    CommuneId = fixture.CommuneId,
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();
                return segment.SegmentId;
            }
        });
}
