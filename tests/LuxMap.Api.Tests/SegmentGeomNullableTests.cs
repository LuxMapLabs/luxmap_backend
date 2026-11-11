using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

/// <summary>
/// ROAD-SEGMENT-GEOM-NULL PR 2a — a road known only from the commune's books has no line (<c>road_segment.geom IS NULL</c>). Every
/// reader either answers <c>null</c> for it or leaves it out on purpose; none may answer 500 or make a line up. No API writes such a
/// road yet (PR 2b), so each test plants one straight into the database. The work-order, sync and survey readers are covered next
/// to their own suites (<c>WorkOrderUndrawnSegmentTests</c>, <c>SyncTests</c>, <c>SurveyIngestTests</c>, <c>SurveyProcessingTests</c>).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SegmentGeomNullableTests(AssetImportFixture fixture)
{
    // Far from every other suite's probes, so a bbox here sees only this class's roads.
    private const double Lng = 110.31;
    private const double Lat = 14.31;
    private const double DegreesPerMetre = 1 / 110_600.0;
    private const string Segments = "/api/v1/assets/segments";

    private LightingTestRig Rigs { get; } = new(fixture);

    [Fact]
    public async Task An_undrawn_road_reads_as_has_geometry_false_with_null_wkt_and_is_on_no_bbox_map()
    {
        var client = await fixture.ManagerClientAsync();
        var drawn = await Segment(drawn: true, "probe drawn");
        var undrawn = await Segment(drawn: false, "probe undrawn");

        var inventory = await All(client, Segments);
        var item = inventory.Single(s => Id(s) == undrawn);
        Assert.False(item.GetProperty("has_geometry").GetBoolean());
        Assert.Equal(1000, item.GetProperty("length_m").GetInt32());   // the declared length does not depend on the line
        var drawnItem = inventory.Single(s => Id(s) == drawn);
        Assert.True(drawnItem.GetProperty("has_geometry").GetBoolean());
        Assert.Equal(Keys(drawnItem), Keys(item));

        var detail = await Get(client, $"{Segments}/{undrawn}");
        Null(detail, "geom_wkt");
        Assert.False(detail.GetProperty("segment").GetProperty("has_geometry").GetBoolean());
        var drawnDetail = await Get(client, $"{Segments}/{drawn}");
        Assert.StartsWith("LINESTRING", drawnDetail.GetProperty("geom_wkt").GetString());
        Assert.True(drawnDetail.GetProperty("segment").GetProperty("has_geometry").GetBoolean());

        // The bbox covers where both roads lie. RG-4: an undrawn road has no place on a map — dropped, not null-geometry.
        var bbox = FormattableString.Invariant($"{Lng - .01},{Lat - .01},{Lng + .02},{Lat + .01}");
        var map = await Get(client, $"/api/v1/map/segments?bbox={bbox}&data_source=simulated");
        var onMap = map.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties").GetProperty("segment_id").GetString()).ToArray();
        Assert.Contains(drawn, onMap);
        Assert.DoesNotContain(undrawn, onMap);
    }

    /// <summary>
    /// RG-11's safety net: positions given to poles before the road had a line are measured the first time the line is drawn
    /// (<c>rerouted</c> is true for null → line), and a pole left far from it refuses the whole PUT.
    /// </summary>
    [Fact]
    public async Task Drawing_an_undrawn_road_measures_the_corridor_of_the_poles_already_placed()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Segment(drawn: false, "probe first line");
        var pole = await Pole(road, null, located: true);   // 100 m north of the road's future line

        var refused = await client.PutAsJsonAsync($"{Segments}/{road}", Put(Line(Lat)));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var error = (await Json(refused)).GetProperty("error");
        Assert.Equal("POLE_OFF_SEGMENT", error.GetProperty("code").GetString());
        Assert.Equal([pole], error.GetProperty("details").GetProperty("pole_ids").EnumerateArray().Select(p => p.GetString()));
        Null(await Get(client, $"{Segments}/{road}"), "geom_wkt");   // rolled back: still undrawn

        var accepted = await client.PutAsJsonAsync($"{Segments}/{road}", Put(Line(Lat + 100 * DegreesPerMetre)));

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var detail = await Get(client, $"{Segments}/{road}");
        Assert.StartsWith("LINESTRING", detail.GetProperty("geom_wkt").GetString());
        Assert.True(detail.GetProperty("segment").GetProperty("has_geometry").GetBoolean());
        Assert.Equal(1000, detail.GetProperty("segment").GetProperty("length_m").GetInt32());   // declared, never recomputed from the new line
    }

    /// <summary>RG-11: a position set on a pole of an undrawn road has no route to be measured against — accepted, audited with no distance.</summary>
    [Fact]
    public async Task A_pole_position_on_an_undrawn_road_is_accepted_and_audits_no_segment_distance()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Segment(drawn: false, "probe pole position");
        var unlocated = await Pole(road, null, located: false);
        var located = await Pole(road, null, located: true);

        var first = await client.PutAsJsonAsync($"/api/v1/assets/poles/{unlocated}/location", new { lat = Lat + 5000 * DegreesPerMetre, lng = Lng });
        var moved = await client.PutAsJsonAsync($"/api/v1/assets/poles/{located}/location", new { lat = Lat + 6000 * DegreesPerMetre, lng = Lng });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Null(await Json(first), "moved_m");
        Assert.True((await Json(moved)).GetProperty("moved_m").GetDouble() > 0);
        foreach (var poleId in new[] { unlocated, located })
        {
            var audit = Assert.Single(await fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
                .Where(a => a.EntityType == AuditEntityType.Pole && a.EntityId == poleId).ToListAsync()));
            var after = JsonDocument.Parse(audit.AfterState!).RootElement;
            Assert.Equal(road, after.GetProperty("segment_id").GetString());
            Null(after, "segment_distance_m");
        }
    }

    /// <summary>RG-10: poles on an undrawn road cannot be ordered along it — left out of the diagram and counted apart from the unlocated ones.</summary>
    [Fact]
    public async Task Cabinet_topology_leaves_poles_of_an_undrawn_road_out_and_counts_them_apart_from_the_unlocated()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await Rigs.NewCabinetAsync();
        var feeder = await Rigs.NewFeederAsync(cabinet);
        var drawn = await Rigs.NewSegmentAsync();
        var onDrawn = await Rigs.NewPoleAsync(drawn, feeder);

        var complete = await Get(client, $"/api/v1/map/cabinets/{cabinet}/topology");
        Assert.Equal(0, complete.GetProperty("unlocated_pole_count").GetInt32());
        Assert.Equal(0, complete.GetProperty("unrouted_pole_count").GetInt32());

        var undrawn = await Segment(drawn: false, "probe topology");
        await Pole(undrawn, feeder, located: true);
        await Pole(undrawn, feeder, located: true);
        await Pole(undrawn, feeder, located: false);   // both reasons at once: counted once, as unlocated
        var partial = await Get(client, $"/api/v1/map/cabinets/{cabinet}/topology");

        Assert.Equal(1, partial.GetProperty("unlocated_pole_count").GetInt32());
        Assert.Equal(2, partial.GetProperty("unrouted_pole_count").GetInt32());
        var edge = Assert.Single(partial.GetProperty("features").EnumerateArray());
        Assert.Equal(onDrawn, edge.GetProperty("properties").GetProperty("to_pole_id").GetString());
    }

    [Fact]
    public async Task Nullable_segment_geom_schema_and_rollback_are_explicit()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("segment_geom_nullable_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).Single(id => id.EndsWith("_RoadSegmentGeomNullable", StringComparison.Ordinal));
        const string previous = "20261111033708_AddFixtureSlot";
        Assert.False(db.Database.HasPendingModelChanges());
        await AssertCatalog(db, "YES");

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "segment-geom-null rollback" };
            db.Add(commune);
            await db.SaveChangesAsync();
            var road = new RoadSegment { CommuneId = commune.CommuneId, SegmentName = "undrawn", RoadClass = RoadClass.InterVillage,
                DataSource = DataSource.Simulated, LengthM = 100 };
            db.Add(road);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Cannot roll back RoadSegmentGeomNullable: 1 segment(s)", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCatalog(db, "YES");
            var preserved = await db.Set<RoadSegment>().IgnoreQueryFilters().SingleAsync(s => s.SegmentId == road.SegmentId);
            Assert.Null(preserved.Geom);

            preserved.Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16)]) { SRID = 4326 };
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await migrator.MigrateAsync(previous);
            Assert.DoesNotContain(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCatalog(db, "NO");
            await migrator.MigrateAsync();
            await AssertCatalog(db, "YES");
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }

    private static async Task AssertCatalog(LuxMapDbContext db, string nullable)
    {
        Assert.Equal(nullable, await db.Database.SqlQuery<string>($"SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'road_segment' AND column_name = 'geom'").SingleAsync());
        // The GIST index is untouched by the migration: still there, still on geom.
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'road_segment' AND indexname = 'ix_road_segment_geom' AND indexdef LIKE '%USING gist (geom)'").SingleAsync());
    }

    private Task<string> Segment(bool drawn, string name)
        => Rigs.AsSystemAsync(async db =>
        {
            var segment = new RoadSegment
            {
                SegmentName = name, RoadClass = RoadClass.InterVillage, LengthM = 1000, CommuneId = fixture.CommuneId,
                DataSource = DataSource.Simulated,
                Geom = drawn ? new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + .01, Lat)]) { SRID = 4326 } : null,
            };
            db.Add(segment);
            await db.SaveChangesAsync();
            return segment.SegmentId;
        });

    /// <summary>A pole 100 m north of the probe road's line, in the middle of it; or one with no position.</summary>
    private Task<string> Pole(string segment, string? feeder, bool located)
        => Rigs.AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                CommuneId = fixture.CommuneId, SegmentId = segment, FeederId = feeder,
                FeederSource = feeder is null ? null : TopologySource.Inferred, DataSource = DataSource.Simulated,
                Geom = located ? new Point(Lng + .005, Lat + 100 * DegreesPerMetre) { SRID = 4326 } : null,
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private static string Line(double lat) => FormattableString.Invariant($"LINESTRING({Lng} {lat}, {Lng + .01} {lat})");

    private static object Put(string wkt) => new { segment_name = "probe road", road_class = "inter_village", length_m = 1000, geom_wkt = wkt, data_source = "simulated" };

    private static string? Id(JsonElement item) => item.GetProperty("segment_id").GetString();
    private static string[] Keys(JsonElement value) => value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
    private static void Null(JsonElement value, string key) => Assert.Equal(JsonValueKind.Null, value.GetProperty(key).ValueKind);
    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await Json(response);
    }
    private static async Task<JsonElement[]> All(HttpClient client, string url)
    {
        var items = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var body = await Get(client, url + (url.Contains('?') ? '&' : '?') + $"page={page++}&page_size=200");
            items.AddRange(body.GetProperty("items").EnumerateArray());
            if (items.Count >= body.GetProperty("total").GetInt32()) return items.ToArray();
        }
    }
}
