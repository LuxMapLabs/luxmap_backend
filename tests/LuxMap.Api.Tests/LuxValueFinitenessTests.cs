using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// Protects the RQ1 ground truth from values that are not numbers.
/// </summary>
/// <remarks>
/// This is NOT ordinary input validation. <c>lux_value</c> is what CV-12 scores the system's
/// classification against, so a single <c>NaN</c> turns every mean, deviation and correlation it
/// computes into <c>NaN</c> — with no exception and no log line, just results that are no longer
/// numbers. The row would look perfectly ordinary in any listing.
/// <para>
/// The database is the layer under test on purpose. The API cannot deliver these values today
/// because System.Text.Json rejects the tokens (pinned by <c>JsonNumberHandlingTests</c>), but CSV
/// import (BE-12), sync push (BE-43) and seeding all reach this table without passing through that
/// serializer.
/// </para>
/// <para>
/// ⚠️ <c>lux_value &gt;= 0</c> alone does NOT reject them: PostgreSQL sorts <c>NaN</c> ABOVE every
/// other float, so <c>'NaN' &gt;= 0</c> is true. Both values were verified reaching the table before
/// the constraint was tightened.
/// </para>
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class LuxValueFinitenessTests(AssetSchemaFixture fixture) : IAsyncLifetime
{
    private readonly List<string> createdPoleIds = [];
    private readonly List<string> createdLuxIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
        => await fixture.WriteAsSystemAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            await db.Set<LuxReading>().IgnoreQueryFilters()
                .Where(reading => createdLuxIds.Contains(reading.LuxId)).ExecuteDeleteAsync();

            return await db.Set<Pole>().IgnoreQueryFilters()
                .Where(pole => createdPoleIds.Contains(pole.PoleId)).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });

    private async Task<string> NewPoleAsync()
    {
        var poleId = await fixture.WriteAsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(106.49, 10.97) { SRID = 4326 },
                DataSource = DataSource.CalibrationRig,
            };
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        createdPoleIds.Add(poleId);
        return poleId;
    }

    private Task<int> WriteAsync(string poleId, double luxValue)
        => fixture.WriteAsSystemAsync(async db =>
        {
            db.Set<LuxReading>().Add(new LuxReading
            {
                ClientOpId = Guid.NewGuid().ToString(),
                PoleId = poleId,
                CommuneId = fixture.CommuneId,
                MeasuredAt = DateTime.UtcNow,
                LuxValue = luxValue,
                DataSource = DataSource.CalibrationRig,
                MeasuredBy = "USR-001",
            });

            return await db.SaveChangesAsync();
        });

    public static TheoryData<string, double> NotNumbers => new()
    {
        { "NaN", double.NaN },
        { "positive infinity", double.PositiveInfinity },
        { "negative infinity", double.NegativeInfinity },
    };

    [Theory]
    [MemberData(nameof(NotNumbers))]
    public async Task A_value_that_is_not_a_number_never_reaches_the_RQ1_ground_truth(
        string label, double luxValue)
    {
        var poleId = await NewPoleAsync();

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(poleId, luxValue));

        // 23514 = check_violation. Asserting the SQLSTATE rather than the message keeps this from
        // passing on some unrelated failure that happens to throw the same exception type.
        Assert.Contains("23514", failure.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(
            "ck_lux_reading_value_non_negative",
            failure.InnerException?.Message ?? string.Empty,
            StringComparison.Ordinal);

        Assert.NotNull(label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12.4)]
    [InlineData(99999)]
    public async Task Real_measurements_are_still_accepted_including_implausible_ones(double luxValue)
    {
        // The counterpart, and the reason it is worth writing: tightening a constraint is only safe
        // if it did not also start refusing real data. 99999 lux is implausible for road lighting and
        // is deliberately still accepted — FO-14 measures once in the field, so a rejected reading is
        // gone for good, while an implausible one stays visible to analysis.
        var poleId = await NewPoleAsync();

        var written = await WriteAsync(poleId, luxValue);

        Assert.Equal(1, written);

        var stored = await fixture.QueryAsync(db => db.Set<LuxReading>()
            .IgnoreQueryFilters()
            .Where(reading => reading.PoleId == poleId)
            .Select(reading => reading.LuxId)
            .ToListAsync());

        createdLuxIds.AddRange(stored);
    }
}
