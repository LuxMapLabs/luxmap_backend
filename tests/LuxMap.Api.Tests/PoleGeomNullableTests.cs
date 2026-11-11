using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
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
/// POLE-GEOM-NULL PR 2a — a pole known only from the commune's books has no position (<c>pole.geom IS NULL</c>). Every
/// reader either answers <c>null</c> for it or leaves it out on purpose; none may answer 500 or make a point up. No API
/// writes such a pole yet (PR 2b), so each test plants one straight into the database.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleGeomNullableTests(AssetImportFixture fixture)
{
    private LightingTestRig Rigs { get; } = new(fixture);

    [Fact]
    public async Task Unlocated_pole_reads_as_null_in_the_inventory_and_is_absent_from_the_bbox_map()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();
        var feeder = await Rigs.NewFeederAsync(await Rigs.NewCabinetAsync());
        var located = await Pole(road, feeder, located: true);
        var unlocated = await Pole(road, feeder, located: false);

        var inventory = await All(client, "/api/v1/assets/poles");
        var item = inventory.Single(p => Id(p) == unlocated);
        Null(item, "location");
        Assert.Equal(road, item.GetProperty("segment_id").GetString());
        Assert.Equal(Keys(inventory.Single(p => Id(p) == located)), Keys(item));

        var detail = await Get(client, $"/api/v1/assets/poles/{unlocated}");
        Null(detail, "geom_wkt");
        Null(detail.GetProperty("pole"), "location");
        Assert.StartsWith("POINT", (await Get(client, $"/api/v1/assets/poles/{located}")).GetProperty("geom_wkt").GetString());

        var wired = await All(client, $"/api/v1/assets/feeders/{feeder}/poles");
        Assert.Equal(new[] { located, unlocated }, wired.Select(Id));
        Null(wired[1], "lat");
        Null(wired[1], "lng");
        Assert.Equal(Keys(wired[0]), Keys(wired[1]));

        // The bbox covers where every rig pole stands; the pole with no position is on no map.
        var map = await Get(client, "/api/v1/map/poles?bbox=109.499,13.499,109.501,13.501&data_source=simulated");
        var onMap = map.GetProperty("features").EnumerateArray().Select(f => Id(f.GetProperty("properties"))).ToArray();
        Assert.Contains(located, onMap);
        Assert.DoesNotContain(unlocated, onMap);

        var mapDetail = await Get(client, $"/api/v1/map/poles/{unlocated}");
        Null(mapDetail, "location");
        Assert.Equal(road, mapDetail.GetProperty("segment_id").GetString());
        Assert.Equal("unknown", mapDetail.GetProperty("current_status").GetProperty("fixture_status").GetString());
    }

    [Fact]
    public async Task Cabinet_topology_leaves_unlocated_poles_out_and_counts_them()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await Rigs.NewCabinetAsync();
        var feeder = await Rigs.NewFeederAsync(cabinet);
        var road = await Rigs.NewSegmentAsync();
        var located = await Pole(road, feeder, located: true);

        var complete = await Get(client, $"/api/v1/map/cabinets/{cabinet}/topology");
        Assert.Equal(0, complete.GetProperty("unlocated_pole_count").GetInt32());
        Assert.Equal("FeatureCollection", complete.GetProperty("type").GetString());

        await Pole(road, feeder, located: false);
        await Pole(road, feeder, located: false);
        var partial = await Get(client, $"/api/v1/map/cabinets/{cabinet}/topology");
        Assert.Equal(2, partial.GetProperty("unlocated_pole_count").GetInt32());
        var edge = Assert.Single(partial.GetProperty("features").EnumerateArray());
        Assert.Equal(located, edge.GetProperty("properties").GetProperty("to_pole_id").GetString());
    }

    [Fact]
    public async Task Qr_is_issued_for_an_unlocated_pole_but_the_public_form_is_told_and_refused()
    {
        var client = await fixture.ManagerClientAsync();
        var unlocated = await Pole(null, null, located: false);
        var located = await Pole(null, null, located: true);
        var anonymous = fixture.CreateClient();

        var issued = await client.PostAsJsonAsync("/api/v1/qr-codes", new { pole_id = unlocated });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var code = (await Json(issued)).GetProperty("code").GetString();

        var lookup = await Json(await anonymous.PostAsJsonAsync("/api/v1/public/qr/lookup", new { code }));
        Assert.False(lookup.GetProperty("can_report").GetBoolean());
        Assert.Equal("pole_location_required", lookup.GetProperty("reason").GetString());

        using var form = new MultipartFormDataContent
        {
            { new StringContent(code!), "code" }, { new StringContent("lamp_out"), "kind" },
            // Whole numbers on purpose: on a host with a decimal-comma locale the form binder misreads "13.5" (seen on en_VN).
            { new StringContent("13"), "lat" }, { new StringContent("109"), "lng" },
            { new StringContent(Guid.NewGuid().ToString()), "client_op_id" },
        };
        var refused = await anonymous.PostAsync("/api/v1/public/reports", form);
        Assert.True(refused.StatusCode == HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        var error = (await Json(refused)).GetProperty("error");
        Assert.Equal("POLE_LOCATION_REQUIRED", error.GetProperty("code").GetString());
        Assert.Equal("citizen_report", error.GetProperty("details").GetProperty("operation").GetString());
        Assert.Empty(await All(client, $"/api/v1/citizen-reports?pole_id={unlocated}"));

        var other = (await Json(await client.PostAsJsonAsync("/api/v1/qr-codes", new { pole_id = located }))).GetProperty("code").GetString();
        var open = await Json(await anonymous.PostAsJsonAsync("/api/v1/public/qr/lookup", new { code = other }));
        Assert.True(open.GetProperty("can_report").GetBoolean());
        Null(open, "reason");
    }

    [Fact]
    public async Task Nullable_pole_geom_schema_and_rollback_are_explicit()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("pole_geom_nullable_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).Single(id => id.EndsWith("_PoleGeomNullable", StringComparison.Ordinal));
        const string previous = "20261109151415_SurveyRecurringSchedules";
        Assert.False(db.Database.HasPendingModelChanges());
        await AssertCatalog(db, "YES");

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "pole-geom-null rollback" };
            db.Add(commune);
            await db.SaveChangesAsync();
            var pole = new Pole { CommuneId = commune.CommuneId, DataSource = DataSource.Simulated };
            db.Add(pole);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Cannot roll back PoleGeomNullable: 1 pole(s)", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCatalog(db, "YES");
            var preserved = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole.PoleId);
            Assert.Null(preserved.Geom);

            preserved.Geom = new Point(108, 16) { SRID = 4326 };
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
        Assert.Equal(nullable, await db.Database.SqlQuery<string>($"SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'pole' AND column_name = 'geom'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'pole' AND indexname = 'ix_pole_geom' AND indexdef LIKE '%USING gist (geom)'").SingleAsync());
    }

    private Task<string> Pole(string? segment, string? feeder, bool located)
        => Rigs.AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                CommuneId = fixture.CommuneId, SegmentId = segment, FeederId = feeder,
                FeederSource = feeder is null ? null : TopologySource.Inferred, DataSource = DataSource.Simulated,
                Geom = located ? new Point(109.5, 13.5) { SRID = 4326 } : null,
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private static string? Id(JsonElement pole) => pole.GetProperty("pole_id").GetString();
    private static string[] Keys(JsonElement value) => value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
    private static void Null(JsonElement value, string key) => Assert.Equal(JsonValueKind.Null, value.GetProperty(key).ValueKind);
    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
