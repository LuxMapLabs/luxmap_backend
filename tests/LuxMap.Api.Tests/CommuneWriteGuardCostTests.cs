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
/// tight threshold on a shared development machine fails for reasons unrelated to the code. What they
/// DO assert is that the run measured something real — see the anchors at the end of the paired test.
/// </para>
/// <para>
/// ⚠️ <b>Excluded from <c>dotnet test</c> by default</b>, via <c>Category=Benchmark</c> and the filter
/// in <c>luxmap.runsettings</c>. A benchmark is not a regression test: this one writes 64,000 rows per
/// run, which is slow on every pass that gains nothing from it, and its output is a number for a
/// person to read rather than a pass/fail. Run it deliberately:
/// <code>dotnet test --settings luxmap.benchmark.runsettings</code>
/// A command-line <c>--filter</c> will NOT work: VSTest ANDs it with the runsettings filter and the
/// two cancel out. Verified, not assumed — the naive command matched zero tests.
/// </para>
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class CommuneWriteGuardCostTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    private const int TrackedEntities = 1000;

    /// <summary>Paired A/B rounds that count toward the statistics.</summary>
    private const int Pairs = 30;

    /// <summary>
    /// Pairs run and thrown away first, so setup and plan caching are not charged to a side.
    /// </summary>
    /// <remarks>
    /// Eight, raised from two after reading a run's raw table: the guard-off column fell from ~90 ms
    /// to ~55 ms over the first six pairs and only settled from the seventh. Two pairs left the
    /// system still warming, which put both the largest positive delta and most of the negative ones
    /// inside the counted sample and widened the spread. The number was chosen from where the column
    /// flattens, BEFORE the affected run was re-analysed — picking a cut-off after seeing which one
    /// rescues a result is how a measurement becomes an argument.
    /// </remarks>
    private const int WarmUpPairs = 8;

    [Fact]
    [Trait("Category", "Benchmark")]
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
    /// The only figure that may be quoted as "what the guard costs": PAIRED A/B on the same 1000-row
    /// write, once with the guard running and once with it skipped.
    /// </summary>
    /// <remarks>
    /// The hypothesis is that the guard adds nothing, because <c>SaveChanges</c> runs
    /// <c>DetectChanges</c> regardless and the guard merely pulls that pass forward.
    /// <para>
    /// ⚠️ <b>Measured as PAIRS, and the difference is taken inside each pair.</b> Subtracting a median
    /// of all the A runs from a median of all the B runs is not the same statistic: it lets machine
    /// drift between the start and the end of the run land entirely on whichever side happened to be
    /// scheduled there. Two writes run back to back share their conditions, so <c>delta_i = A_i −
    /// B_i</c> cancels the drift instead of absorbing it. Only the distribution of those deltas is
    /// reported.
    /// </para>
    /// <para>
    /// B (guard off) is <c>EnterUnscopedSystemWriteBackdoor</c>, opened OUTSIDE the timed region so
    /// the increment and the disposable are not charged to the measurement. It makes
    /// <c>EnforceCommuneWriteScope</c> return before <c>CommuneWriteGuard.Enforce</c> is even called,
    /// which is what "the guard is absent" has to mean. A (guard on) runs under a real commune claim
    /// so the guard walks all 1000 entries.
    /// </para>
    /// <para>
    /// This test only PRINTS. Whether the numbers may be written into CLAUDE.md is decided by a gate
    /// stated in the ticket — at least 27 of 30 pairs positive, and IQR below the median — and that
    /// judgement is not the test's to make, which is why it asserts nothing about the delta.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task The_MARGINAL_cost_of_the_guard_measured_A_B_on_SaveChanges()
    {
        var segmentId = await SeedSegmentAsync();

        // Two discarded pairs: the first writes of a run pay for connection setup and query-plan
        // caching, and charging that to whichever side goes first is how you invent a result.
        for (var warmUp = 0; warmUp < WarmUpPairs; warmUp++)
        {
            await MeasureAsync(segmentId, guardActive: false);
            await MeasureAsync(segmentId, guardActive: true);
        }

        var pairs = new List<(Timed B, Timed A, double Delta)>();

        for (var pair = 0; pair < Pairs; pair++)
        {
            // B first, then A, back to back — the pair is the unit of measurement.
            var b = await MeasureAsync(segmentId, guardActive: false);
            var a = await MeasureAsync(segmentId, guardActive: true);
            pairs.Add((b, a, a.Milliseconds - b.Milliseconds));
        }

        var deltas = pairs.Select(pair => pair.Delta).ToList();
        var positive = deltas.Count(delta => delta > 0);
        var median = Median(deltas);
        var (q1, q3) = Quartiles(deltas);
        var iqr = q3 - q1;

        output.WriteLine($"rows per write : {TrackedEntities}   pairs: {Pairs} "
            + $"(after {WarmUpPairs} warm-up pairs)   order within a pair: B then A");
        output.WriteLine(string.Empty);
        output.WriteLine("pair |    B (ms) |    A (ms) | delta (ms)");
        for (var i = 0; i < pairs.Count; i++)
        {
            output.WriteLine(
                $"{i + 1,4} | {pairs[i].B.Milliseconds,9:F2} | {pairs[i].A.Milliseconds,9:F2} | {pairs[i].Delta,10:F2}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"pairs with delta > 0     : {positive}/{Pairs}");
        output.WriteLine($"median delta             : {median:F2} ms");
        output.WriteLine($"IQR delta                : {iqr:F2} ms  (Q1 {q1:F2} .. Q3 {q3:F2})");
        output.WriteLine($"min / max delta          : {deltas.Min():F2} / {deltas.Max():F2} ms");
        output.WriteLine($"median per entity        : {median * 1000 / TrackedEntities:F2} us");
        output.WriteLine($"IQR per entity           : {q1 * 1000 / TrackedEntities:F2} .. "
            + $"{q3 * 1000 / TrackedEntities:F2} us");
        output.WriteLine(string.Empty);

        // For reference ONLY. Never subtract these: see the note on pairing above.
        output.WriteLine(
            "[reference, not used for delta] raw median B "
            + $"{Median(pairs.Select(pair => pair.B.Milliseconds).ToList()):F2} ms, raw median A "
            + $"{Median(pairs.Select(pair => pair.A.Milliseconds).ToList()):F2} ms");

        output.WriteLine(
            "Gate for writing this into CLAUDE.md: positive pairs >= 27/30 AND IQR < median.");

        // ── Anchors ────────────────────────────────────────────────────────────────────────────
        // A measurement that only prints is a silent no-op, and this file would be the worst place in
        // the repository to allow one: it exists to stop somebody arguing the guard is expensive. If
        // a future edit stops it measuring anything, these fail instead of printing a tidy table of
        // meaningless numbers. None of them asserts what the guard COSTS — that judgement is the
        // ticket's gate, not the test's.
        Assert.Equal(Pairs, deltas.Count);

        Assert.All(pairs, pair =>
        {
            // Every write really wrote all 1000 rows: an empty change tracker would time nothing at
            // all and still produce a plausible-looking table.
            Assert.Equal(TrackedEntities, pair.B.RowsWritten);
            Assert.Equal(TrackedEntities, pair.A.RowsWritten);

            // A zero or negative duration means the clock never ran.
            Assert.True(pair.B.Milliseconds > 0, "Guard-off write measured no time at all.");
            Assert.True(pair.A.Milliseconds > 0, "Guard-on write measured no time at all.");
        });
    }

    /// <summary>
    /// Q1 and Q3 by nearest rank on the sorted sample — no interpolation, so the values printed are
    /// observations that really occurred rather than points between them.
    /// </summary>
    private static (double Q1, double Q3) Quartiles(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[sorted.Length / 4], sorted[sorted.Length * 3 / 4]);
    }

    /// <summary>A single timed write: how long <c>SaveChanges</c> took, and how many rows it wrote.</summary>
    /// <remarks>
    /// The row count is carried out of here ON PURPOSE. A benchmark that only prints is a silent
    /// no-op: if a future edit left the change tracker empty, every measurement would collapse to
    /// microseconds and the table would still look like a result. Returning what was actually written
    /// lets the caller assert the run measured the thing it claims to measure.
    /// </remarks>
    private sealed record Timed(double Milliseconds, int RowsWritten);

    private async Task<Timed> MeasureAsync(string segmentId, bool guardActive)
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
            var written = await db.SaveChangesAsync();
            timer.Stop();

            await transaction.RollbackAsync();
            return new Timed(timer.Elapsed.TotalMilliseconds, written);
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
}
