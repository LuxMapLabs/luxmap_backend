using System.Diagnostics;
using System.Security.Claims;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// What <c>CommuneWriteGuard</c> costs on a change tracker the size of a CSV import (BE-12a).
/// </summary>
/// <remarks>
/// The ~3.7 µs per entity once recorded in CLAUDE.md was measured on a handful of entities and, more
/// importantly, measured the wrong thing: it is the WARM <c>ChangeTracker.Entries&lt;T&gt;()</c> pass,
/// not the scope check the guard actually performs.
/// <para>
/// ⚠️ <b>Two different numbers, and only one of them is "the cost of the guard".</b> The parts
/// measured in <see cref="The_parts_of_the_guard_measured_separately"/> are the guard's own work in
/// isolation. But <c>Entries&lt;T&gt;()</c> calls <c>DetectChanges</c>, and <c>SaveChanges</c> was
/// going to call <c>DetectChanges</c> anyway — so the guard may only be MOVING that pass earlier
/// rather than adding one. Adding the parts up would then overstate the real cost, which is exactly
/// the kind of mis-attribution that produced the 3.7 µs figure in the first place, only in the other
/// direction. <see cref="The_MARGINAL_cost_of_the_guard_measured_A_B_on_SaveChanges"/> settles it by
/// measuring the same write with the guard on and off.
/// </para>
/// <para>
/// Assertions are deliberately loose. These print numbers; they are not a performance gate, and a
/// tight threshold on a shared development machine fails for reasons unrelated to the code.
/// </para>
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class CommuneWriteGuardCostTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    private const int TrackedEntities = 1000;

    /// <summary>How many A/B pairs to run. The median is reported; the spread is printed too.</summary>
    private const int Rounds = 5;

    [Fact]
    public async Task The_parts_of_the_guard_measured_separately()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        var segmentId = await SeedSegmentAsync();
        TrackPoles(db, segmentId, TrackedEntities);

        Assert.Equal(TrackedEntities, db.ChangeTracker.Entries<ICommuneScoped>().Count());

        // (1) The first Entries<T>() call after the Adds pays for DetectChanges over the whole graph.
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        var cold = Stopwatch.StartNew();
        var entries = db.ChangeTracker.Entries<ICommuneScoped>().ToList();
        cold.Stop();

        // (2) A second call with nothing changed in between: traversal without a real diff to do.
        var warm = Stopwatch.StartNew();
        _ = db.ChangeTracker.Entries<ICommuneScoped>().ToList();
        warm.Stop();

        // (3) The guard's OWN work: one scope check per entity. This is what 3.7 µs was claimed to be.
        var communeScope = CommuneScope.ForCommunes([fixture.CommuneId]);
        var checks = Stopwatch.StartNew();
        var allowed = entries.Count(entry => communeScope.Allows(entry.Entity.CommuneId));
        checks.Stop();

        Assert.Equal(TrackedEntities, allowed);

        output.WriteLine($"tracked entities                     : {TrackedEntities}");
        output.WriteLine($"(1) Entries<T>() incl. DetectChanges : {Per(cold)} us/entity");
        output.WriteLine($"(2) Entries<T>() warm                : {Per(warm)} us/entity  <- the old 3.7 figure");
        output.WriteLine($"(3) scope check                      : {Per(checks)} us/entity  <- the guard's own work");
        output.WriteLine(
            "These are PARTS, not a total. (1) may be work SaveChanges would have done anyway; see "
            + "the A/B test for the marginal cost.");

        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// The only figure that may be quoted as "what the guard costs": the same 1000-row write, once
    /// with the guard running and once with it skipped.
    /// </summary>
    /// <remarks>
    /// The hypothesis being tested is that the guard adds nothing, because <c>SaveChanges</c> runs
    /// <c>DetectChanges</c> regardless and the guard merely pulls that pass forward. Both sides do
    /// identical database work and both roll back, so the difference is the guard and nothing else.
    /// <para>
    /// The A side opens <c>EnterUnscopedSystemWriteBackdoor</c>, which makes
    /// <c>CommuneWriteGuard.Enforce</c> return on its first line. The B side runs under a real
    /// commune claim so the guard walks all 1000 entries. Rounds alternate and the MEDIAN is taken:
    /// a single pair on a shared machine measures whatever else was running.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_MARGINAL_cost_of_the_guard_measured_A_B_on_SaveChanges()
    {
        var segmentId = await SeedSegmentAsync();

        var withoutGuard = new List<double>();
        var withGuard = new List<double>();

        // One discarded pair first: the first write of a run pays for connection setup and query-plan
        // caching, and charging that to whichever side happened to go first is how you invent a result.
        await MeasureAsync(segmentId, guardActive: false);
        await MeasureAsync(segmentId, guardActive: true);

        for (var round = 0; round < Rounds; round++)
        {
            withoutGuard.Add(await MeasureAsync(segmentId, guardActive: false));
            withGuard.Add(await MeasureAsync(segmentId, guardActive: true));
        }

        var offMedian = Median(withoutGuard);
        var onMedian = Median(withGuard);
        var marginal = (onMedian - offMedian) * 1000 / TrackedEntities;

        output.WriteLine($"rows per write : {TrackedEntities}   rounds: {Rounds} (median reported)");
        output.WriteLine($"SaveChanges, guard OFF (backdoor) : {offMedian:F1} ms   [{Spread(withoutGuard)}]");
        output.WriteLine($"SaveChanges, guard ON             : {onMedian:F1} ms   [{Spread(withGuard)}]");
        output.WriteLine($"MARGINAL cost of the guard        : {onMedian - offMedian:F1} ms total, "
            + $"{marginal:F2} us/entity");
        output.WriteLine(
            "This is the number to quote. The separate parts add up to more because Entries<T>() "
            + "triggers a DetectChanges that SaveChanges would otherwise have run itself.");

        // No threshold on the delta: it can legitimately land at or below zero if the guard only moves
        // the DetectChanges pass. What IS asserted is that the guard does not multiply the write.
        Assert.True(
            onMedian < (offMedian * 2) + 50,
            $"Guard ON ({onMedian:F1} ms) is disproportionate to guard OFF ({offMedian:F1} ms).");
    }

    /// <summary>One timed write of <see cref="TrackedEntities"/> poles, rolled back. Returns milliseconds.</summary>
    private async Task<double> MeasureAsync(string segmentId, bool guardActive)
    {
        SetPrincipal(guardActive ? fixture.CommuneId : null);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        // The Adds are OUTSIDE the timed region on both sides, so only SaveChanges is compared.
        var backdoor = guardActive ? null : db.EnterUnscopedSystemWriteBackdoor();
        try
        {
            TrackPoles(db, segmentId, TrackedEntities);

            var timer = Stopwatch.StartNew();
            await db.SaveChangesAsync();
            timer.Stop();

            await transaction.RollbackAsync();
            return timer.Elapsed.TotalMilliseconds;
        }
        finally
        {
            backdoor?.Dispose();
            SetPrincipal(null);
        }
    }

    /// <summary>
    /// Installs an ambient principal so <c>ICommuneScopeAccessor</c> reports a real scope outside an
    /// HTTP request. <c>null</c> clears it, which is what the backdoor side wants.
    /// </summary>
    private void SetPrincipal(string? communeId)
    {
        var accessor = fixture.Services.GetRequiredService<IHttpContextAccessor>();

        if (communeId is null)
        {
            accessor.HttpContext = null;
            return;
        }

        List<Claim> claims =
        [
            new(AuthClaims.Subject, "USR-COST"),
            new(AuthClaims.Role, ContractEnum.ToDbValue(UserRole.Administrator)),
            new(AuthClaims.CommuneIds, communeId),
        ];

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
    }

    private static void TrackPoles(LuxMapDbContext db, string segmentId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.Set<Pole>().Add(new Pole
            {
                ExternalRef = $"COST-{Guid.NewGuid():N}",
                SegmentId = segmentId,
                CommuneId = db.CurrentCommuneScope.CommuneIds.FirstOrDefault() ?? CommuneOf(db),
                Geom = new Point(106.49 + (i * 0.0001), 10.97) { SRID = 4326 },
                DataSource = DataSource.PublicImagery,
            });
        }
    }

    /// <summary>Falls back to the fixture's commune when no principal is installed (the backdoor side).</summary>
    private static string CommuneOf(LuxMapDbContext db)
        => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.ExternalRef!.StartsWith("COST-SEG-"))
            .Select(segment => segment.CommuneId)
            .First();

    private async Task<string> SeedSegmentAsync()
        => await fixture.QueryAsync(async db =>
        {
            var existing = await db.Set<RoadSegment>().IgnoreQueryFilters()
                .Where(segment => segment.ExternalRef!.StartsWith("COST-SEG-")
                    && segment.CommuneId == fixture.CommuneId)
                .Select(segment => segment.SegmentId)
                .FirstOrDefaultAsync();

            if (existing is not null)
            {
                return existing;
            }

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

    private static string Per(Stopwatch timer)
        => (timer.Elapsed.TotalMicroseconds / TrackedEntities).ToString("F2");

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private static string Spread(List<double> values)
        => $"min {values.Min():F1} / max {values.Max():F1}";
}
