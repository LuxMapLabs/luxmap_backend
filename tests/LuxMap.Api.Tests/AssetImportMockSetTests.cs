using System.Text.Json;
using System.Text.Json.Nodes;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12a acceptance: load the whole FO-26 mock set — 3 segments, 103 poles, 103 fixtures — through
/// the real import endpoint.
/// </summary>
/// <remarks>
/// ⚠️ <b>The mock cannot be loaded as it stands, and that is a finding, not a test problem.</b>
/// <c>mock-poles.geojson</c> carries 15 properties and <c>mock-segments.geojson</c> seven; between
/// them they are missing four fields the schema requires or the import keys on:
/// <list type="bullet">
/// <item><c>external_ref</c> — nothing in either file. Supplied here from the mock's own
/// <c>pole_id</c> / <c>segment_id</c>, which is what a real migration would do: the mock ids ARE the
/// authority's codes until real ones exist.</item>
/// <item><c>commune_id</c> — absent from the segments file entirely, and the poles file names
/// <c>COM-001</c>, which belongs to the seeded study site rather than this test's own commune.
/// Overwritten so the run cleans up after itself.</item>
/// <item><c>data_source</c> — absent from both. Set to <c>public_imagery</c>, which is what the
/// Branch C decision says night-imagery assets are.</item>
/// <item><c>feeder_id</c> — absent, and NOT supplied here. That is registered drift 23: without it
/// CV-15 has no circuits to cluster along. Inventing values would hide the gap.</item>
/// </list>
/// So this proves the import can rebuild the asset layer of FO-26, and it documents exactly what the
/// mock set still owes before BE-39 can seed it for real.
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class AssetImportMockSetTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task The_whole_FO26_asset_layer_loads_through_the_import_endpoint()
    {
        var client = await fixture.AdminClientAsync();
        var tag = $"FO26{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var segments = await AssetImportTests.ImportAsync(
            client, "segments", "mock-segments.geojson", PrepareSegments(tag));

        Assert.Equal(0, segments.GetProperty("failed").GetInt32());
        Assert.Equal(3, segments.GetProperty("inserted").GetInt32());

        var poles = await AssetImportTests.ImportAsync(
            client, "poles", "mock-poles.geojson", PreparePoles(tag));

        Assert.Equal(0, poles.GetProperty("failed").GetInt32());
        Assert.Equal(103, poles.GetProperty("inserted").GetInt32());

        var fixtures = await AssetImportTests.ImportAsync(
            client, "fixtures", "mock-fixtures.geojson", PrepareFixtures(tag));

        Assert.Equal(0, fixtures.GetProperty("failed").GetInt32());
        Assert.Equal(103, fixtures.GetProperty("inserted").GetInt32());

        var stored = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(pole => pole.ExternalRef!.StartsWith(tag))
            .Select(pole => new { pole.ExternalRef, pole.Geom, pole.SegmentId, pole.FeederId })
            .ToListAsync());

        Assert.Equal(103, stored.Count);

        // Coordinates survived as [longitude, latitude] in EPSG:4326 — the study site, not a
        // transposed pair somewhere in the Indian Ocean.
        Assert.All(stored, pole =>
        {
            Assert.Equal(4326, pole.Geom.SRID);
            Assert.InRange(pole.Geom.X, 106.0, 107.0);
            Assert.InRange(pole.Geom.Y, 10.0, 11.0);
        });

        // The gap this test is not allowed to paper over.
        Assert.All(stored, pole => Assert.Null(pole.FeederId));
        output.WriteLine(
            $"Loaded {stored.Count} poles across {stored.Select(p => p.SegmentId).Distinct().Count()} segments. "
            + "All 103 have feeder_id NULL — drift 23, the mock set carries no circuit at all.");

        // Re-running is an UPDATE, not 103 duplicates. This is what external_ref bought.
        var again = await AssetImportTests.ImportAsync(
            client, "poles", "mock-poles.geojson", PreparePoles(tag));

        Assert.Equal(0, again.GetProperty("inserted").GetInt32());
        Assert.Equal(103, again.GetProperty("updated").GetInt32());
        Assert.Equal(103, await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .CountAsync(pole => pole.ExternalRef!.StartsWith(tag))));
    }

    /// <summary>
    /// The batch is one transaction: a failure at WRITE time takes the whole file with it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Driven through the DbContext rather than the endpoint, because the failure it stages cannot
    /// be produced by a single HTTP request. Everything checkable — a missing cell, a bad enum, an
    /// unresolvable reference, a foreign commune — is caught during validation, so no ONE upload
    /// reaches the write with a bad row in it.
    /// <para>
    /// That is NOT the same as saying no row-level failure can reach the write. The upsert reads then
    /// adds, and those are separate statements: two imports of the same file at once both read
    /// nothing, both add, and the loser hits the unique index inside <c>SaveChanges</c>. See the note
    /// on <c>AssetImportService</c> — a known limitation, not a design guarantee. This test covers the
    /// mechanism that keeps that case harmless: many valid rows plus one the database refuses, one
    /// transaction, nothing survives.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task One_failing_row_at_write_time_rolls_the_entire_batch_back()
    {
        var tag = $"RB{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            for (var i = 0; i < 10; i++)
            {
                db.Set<RoadSegment>().Add(NewSegment(tag, i));
            }

            // The eleventh repeats the first external_ref in the same commune, which
            // ux_road_segment_commune_external_ref refuses. Ten good rows are already tracked.
            db.Set<RoadSegment>().Add(NewSegment(tag, 0));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var survivors = await fixture.QueryAsync(inner => inner.Set<RoadSegment>().IgnoreQueryFilters()
            .CountAsync(segment => segment.ExternalRef!.StartsWith(tag)));

        Assert.Equal(0, survivors);
    }

    private RoadSegment NewSegment(string tag, int index) => new()
    {
        ExternalRef = $"{tag}-{index}",
        SegmentName = $"Rollback probe {index}",
        RoadClass = RoadClass.InterVillage,
        LengthM = 100,
        Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
        CommuneId = fixture.CommuneId,
        DataSource = DataSource.PublicImagery,
    };

    /// <summary>Adds the fields the mock set does not carry, and says so in the test name above.</summary>
    private string PrepareSegments(string tag) => Rewrite("mock-segments.geojson", (properties, _) =>
    {
        properties["external_ref"] = $"{tag}-{properties["segment_id"]!.GetValue<string>()}";
        properties["commune_id"] = fixture.CommuneId;
        properties["data_source"] = "public_imagery";
    });

    private string PreparePoles(string tag) => Rewrite("mock-poles.geojson", (properties, _) =>
    {
        properties["external_ref"] = $"{tag}-{properties["pole_id"]!.GetValue<string>()}";
        properties["segment_external_ref"] = $"{tag}-{properties["segment_id"]!.GetValue<string>()}";
        properties["commune_id"] = fixture.CommuneId;
        properties["data_source"] = "public_imagery";
    });

    /// <summary>
    /// Fixtures come from the POLE features: <c>power_source</c>, <c>fixture_type</c>,
    /// <c>lamp_watt</c>, <c>install_date</c> and <c>warranty_expiry</c> live in the pole's properties
    /// in the mock, because section 2.1 flattens the active lamp into the pole. Import puts them back
    /// on the table that owns them.
    /// </summary>
    private string PrepareFixtures(string tag) => Rewrite("mock-poles.geojson", (properties, _) =>
    {
        properties["pole_external_ref"] = $"{tag}-{properties["pole_id"]!.GetValue<string>()}";
        properties["data_source"] = "public_imagery";
    });

    private static string Rewrite(string mockFile, Action<JsonObject, JsonNode?> edit)
    {
        var path = Path.Combine(RepositoryRoot(), "mocks", mockFile);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        foreach (var feature in root["features"]!.AsArray())
        {
            edit(feature!["properties"]!.AsObject(), feature["geometry"]);
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "mocks")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("mocks/ not found above the test binaries.");
    }
}
