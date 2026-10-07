using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// CABINET — the main electrical cabinet as an asset of its own, a device mounted in one (drift CAB-1…CAB-8).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ SELF-SIGNED shape, not yet in the Contract. Expectations are LITERALS — key sets, error codes, constraint
/// names — so a test cannot agree with whatever the code happens to do.
/// </para>
/// <para>
/// The database rule (CAB-4) is proven with RAW SQL, not through the service: they exist because the only
/// writer of <c>iot_node</c> and <c>feeder_control</c> today is a SQL script, and a test through the service would
/// stay green with the constraints dropped.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class CabinetTests(AssetImportFixture fixture)
{
    private const string Cabinets = "/api/v1/assets/cabinets";
    private const string Feeders = "/api/v1/assets/feeders";
    private const string MapCabinets = "/api/v1/map/cabinets";
    private const string MapNodes = "/api/v1/map/iot-nodes";

    /// <summary>A box of its own, away from the mock set and from the other map tests.</summary>
    private const string Box = "?bbox=108.70,12.70,108.90,12.90";

    private const double Lng = 108.8;
    private const double Lat = 12.8;

    // ── Inventory read shape (CAB-8) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cabinet_row_carries_exactly_the_decided_keys()
    {
        var client = await fixture.ManagerClientAsync();
        var externalRef = Tag();
        var cabinetId = await CreateCabinetAsync(client, fixture.CommuneId, externalRef);

        var row = await FindAsync(client, $"{Cabinets}?page_size=200", "cabinet_id", cabinetId);

        string[] expected =
        [
            "cabinet_id", "external_ref", "cabinet_name", "commune_id", "data_source", "location",
            "feeder_ids", "iot_node_id", "updated_at", "updated_by", "updated_by_name",
        ];
        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], Keys(row));

        Assert.Equal(externalRef, row.GetProperty("external_ref").GetString());
        Assert.Equal("field", row.GetProperty("data_source").GetString());
        Assert.Equal(Lat, row.GetProperty("location").GetProperty("lat").GetDouble(), 6);
        Assert.Equal(Lng, row.GetProperty("location").GetProperty("lng").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Array, row.GetProperty("feeder_ids").ValueKind);
        Assert.Empty(row.GetProperty("feeder_ids").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("iot_node_id").ValueKind);

        // Device state is the map layer's answer, never this one's (Contract 5.3.1: one answer per question).
        foreach (var operational in new[] { "node_status", "last_report_at", "supports_remote_control" })
        {
            Assert.False(row.TryGetProperty(operational, out _), operational);
        }
    }

    [Fact]
    public async Task The_detail_wraps_the_row_with_its_wkt_and_creation_time()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await CreateCabinetAsync(client, fixture.CommuneId);

        var detail = await GetAsync(client, $"{Cabinets}/{cabinetId}");

        Assert.Equal(["cabinet", "created_at", "geom_wkt"], Keys(detail));
        Assert.Equal(cabinetId, detail.GetProperty("cabinet").GetProperty("cabinet_id").GetString());
        Assert.Equal("POINT (108.8 12.8)", detail.GetProperty("geom_wkt").GetString());
    }

    [Fact]
    public async Task A_cabinet_of_another_commune_is_a_404_like_any_asset()
    {
        var client = await fixture.ManagerClientAsync();
        var foreign = await NewCabinetAsync(fixture.ForeignCommuneId);

        var response = await client.GetAsync($"{Cabinets}/{foreign}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ASSET_NOT_FOUND", await CodeAsync(response));
    }

    [Fact]
    public async Task Creating_a_cabinet_in_a_commune_outside_the_scope_is_a_403()
    {
        var client = await fixture.ManagerClientAsync();

        var response = await client.PostAsJsonAsync(Cabinets, CabinetBody(fixture.ForeignCommuneId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("COMMUNE_FORBIDDEN", await CodeAsync(response));
    }

    [Fact]
    public async Task The_mounted_device_and_the_feeders_are_listed_on_the_cabinet()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        var first = await NewFeederAsync(fixture.CommuneId, cabinetId);
        var second = await NewFeederAsync(fixture.CommuneId, cabinetId);
        var node = await NewNodeAsync(cabinetId);

        var cabinet = (await GetAsync(client, $"{Cabinets}/{cabinetId}")).GetProperty("cabinet");

        Assert.Equal([first, second], Strings(cabinet.GetProperty("feeder_ids")));
        Assert.Equal(node, cabinet.GetProperty("iot_node_id").GetString());
    }

    /// <summary>
    /// 🔴 <c>feeder_ids</c> is in id order, not text order — and the length tiebreaker is what decides it, because
    /// one batch shares one <c>created_at</c> (CLAUDE.md section 0). The two ids straddle a width boundary and are
    /// written in ONE statement, so <c>created_at</c> ties; as text <c>FDR-1000000</c> would sort first.
    /// </summary>
    [Fact]
    public async Task Feeder_ids_are_in_id_order_across_a_width_boundary_on_both_surfaces()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        var (shorter, longer) = await FreeStraddlingFeederIdsAsync();

        try
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO feeder (feeder_id, feeder_name, commune_id, cabinet_id) VALUES
                ({longer}, 'order probe', {fixture.CommuneId}, {cabinetId}),
                ({shorter}, 'order probe', {fixture.CommuneId}, {cabinetId})
                """));

            var inventory = (await GetAsync(client, $"{Cabinets}/{cabinetId}")).GetProperty("cabinet");
            Assert.Equal([shorter, longer], Strings(inventory.GetProperty("feeder_ids")));

            var map = Find(await GetAsync(client, MapCabinets + Box), "cabinet_id", cabinetId);
            Assert.Equal([shorter, longer], Strings(map.GetProperty("properties").GetProperty("feeder_ids")));
        }
        finally
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"DELETE FROM feeder WHERE feeder_id IN ({shorter}, {longer})"));
        }
    }

    // ── Feeder ↔ cabinet (CAB-6) ────────────────────────────────────────────────────────────────

    /// <summary>The question that started CABINET: where is the feeder's cabinet, from the list alone.</summary>
    [Fact]
    public async Task A_feeder_row_carries_its_cabinet_with_the_point()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await CreateCabinetAsync(client, fixture.CommuneId);
        var feederId = await CreateFeederAsync(client, cabinetId);
        var bare = await CreateFeederAsync(client, null);

        var row = await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", feederId);
        var cabinet = row.GetProperty("cabinet");

        Assert.Equal(["cabinet_id", "cabinet_name", "location"], Keys(cabinet));
        Assert.Equal(cabinetId, cabinet.GetProperty("cabinet_id").GetString());
        Assert.Equal(Lat, cabinet.GetProperty("location").GetProperty("lat").GetDouble(), 6);
        Assert.Equal(Lng, cabinet.GetProperty("location").GetProperty("lng").GetDouble(), 6);

        var none = await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", bare);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("cabinet").ValueKind);
    }

    /// <summary>
    /// 🔴 Absent KEEPS, <c>null</c> detaches, an id moves (CAB-6) — the <c>note</c> rule, not the pole <c>feeder_id</c>
    /// rule. A form written before cabinets existed must not detach every feeder it renames.
    /// </summary>
    [Fact]
    public async Task Replacing_a_feeder_without_the_cabinet_key_keeps_the_cabinet()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await CreateCabinetAsync(client, fixture.CommuneId);
        var other = await CreateCabinetAsync(client, fixture.CommuneId);
        var feederId = await CreateFeederAsync(client, cabinetId);

        await PutAsync(client, $"{Feeders}/{feederId}", new { feeder_name = "renamed, cabinet key absent" });
        Assert.Equal(cabinetId, await FeederCabinetAsync(feederId));

        await PutAsync(client, $"{Feeders}/{feederId}", new { feeder_name = "moved", cabinet_id = other });
        Assert.Equal(other, await FeederCabinetAsync(feederId));

        await PutAsync(client, $"{Feeders}/{feederId}", new { feeder_name = "detached", cabinet_id = (string?)null });
        Assert.Null(await FeederCabinetAsync(feederId));
    }

    [Fact]
    public async Task A_cabinet_of_another_commune_cannot_feed_the_feeder()
    {
        var client = await fixture.BothCommunesClientAsync();
        var foreign = await NewCabinetAsync(fixture.ForeignCommuneId);

        var response = await client.PostAsJsonAsync(
            Feeders, new { feeder_name = "cross-commune probe", commune_id = fixture.CommuneId, cabinet_id = foreign });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CROSS_COMMUNE_REFERENCE", await CodeAsync(response));
    }

    [Fact]
    public async Task A_feeder_a_device_switches_cannot_leave_or_change_its_cabinet()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        var other = await NewCabinetAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId, cabinetId);
        var node = await NewNodeAsync(cabinetId);
        await ControlAsync(feederId, node, cabinetId);

        foreach (var target in new[] { null, other })
        {
            var response = await client.PutAsJsonAsync(
                $"{Feeders}/{feederId}", new { feeder_name = "switched", cabinet_id = target });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ErrorAsync(response);
            Assert.Equal("ASSET_IN_USE", error.GetProperty("code").GetString());
            Assert.Equal(node, error.GetProperty("details").GetProperty("iot_node_id").GetString());
        }

        // A rename that leaves the cabinet alone still goes through.
        await PutAsync(client, $"{Feeders}/{feederId}", new { feeder_name = "switched, renamed", cabinet_id = cabinetId });
        Assert.Equal(cabinetId, await FeederCabinetAsync(feederId));
    }

    // ── Cabinet writes ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CAB-5 was DROPPED (Mỹ, 07/10/2026): a cabinet carrying a device may be <c>field</c> data. Pinned so the rule
    /// does not quietly come back; the device itself is still never <c>field</c> (<c>IotNodeTests</c>).
    /// </summary>
    [Fact]
    public async Task A_cabinet_carrying_a_device_may_become_field_data()
    {
        var client = await fixture.ManagerClientAsync();
        var mounted = await NewCabinetAsync(fixture.CommuneId);
        await NewNodeAsync(mounted);

        await PutAsync(client, $"{Cabinets}/{mounted}", CabinetReplacement("field"));

        var cabinet = (await GetAsync(client, $"{Cabinets}/{mounted}")).GetProperty("cabinet");
        Assert.Equal("field", cabinet.GetProperty("data_source").GetString());
    }

    [Fact]
    public async Task A_cabinet_still_holding_a_feeder_or_a_device_is_not_deleted()
    {
        var client = await fixture.ManagerClientAsync();
        var withFeeder = await NewCabinetAsync(fixture.CommuneId);
        await NewFeederAsync(fixture.CommuneId, withFeeder);
        var withDevice = await NewCabinetAsync(fixture.CommuneId);
        await NewNodeAsync(withDevice);
        var empty = await NewCabinetAsync(fixture.CommuneId);

        foreach (var (cabinet, constraint) in new[]
                 {
                     (withFeeder, "fk_feeder_electrical_cabinet_cabinet_id_commune_id"),
                     (withDevice, "fk_iot_node_electrical_cabinet_cabinet_id_commune_id"),
                 })
        {
            var response = await client.DeleteAsync($"{Cabinets}/{cabinet}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await ErrorAsync(response);
            Assert.Equal("ASSET_IN_USE", error.GetProperty("code").GetString());
            Assert.Equal(constraint, error.GetProperty("details").GetProperty("constraint").GetString());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Cabinets}/{empty}")).StatusCode);
    }

    // ── Map layers (CAB-3, CAB-7) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cabinet_without_a_device_is_still_on_the_map_with_exactly_the_decided_keys()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await NewCabinetAsync(fixture.CommuneId, DataSource.Field);
        var feederId = await NewFeederAsync(fixture.CommuneId, cabinetId);

        var feature = Find(await GetAsync(client, MapCabinets + Box), "cabinet_id", cabinetId);

        Assert.False(feature.TryGetProperty("id", out _));
        var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(Lng, coordinates[0].GetDouble(), 6);
        Assert.Equal(Lat, coordinates[1].GetDouble(), 6);

        var properties = feature.GetProperty("properties");
        Assert.Equal(["cabinet_id", "cabinet_name", "commune_id", "feeder_ids", "iot_node_id"], Keys(properties));
        Assert.Equal([feederId], Strings(properties.GetProperty("feeder_ids")));
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("iot_node_id").ValueKind);
    }

    [Fact]
    public async Task The_testbed_cabinet_is_hidden_by_default_and_the_bbox_is_required()
    {
        var client = await fixture.ManagerClientAsync();
        var testbed = await NewCabinetAsync(fixture.CommuneId, DataSource.CalibrationRig);

        Assert.DoesNotContain(testbed, Ids(await GetAsync(client, MapCabinets + Box)));
        Find(await GetAsync(client, MapCabinets + Box + "&data_source=calibration_rig"), "cabinet_id", testbed);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(MapCabinets)).StatusCode);

        var foreign = await client.GetAsync($"{MapCabinets}{Box}&commune_id={fixture.ForeignCommuneId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    /// <summary>CAB-3 — the device is drawn where its cabinet stands, and follows the cabinet when it moves.</summary>
    [Fact]
    public async Task A_device_is_drawn_at_its_cabinet_and_follows_it()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        var node = await NewNodeAsync(cabinetId);

        var before = Find(await GetAsync(client, MapNodes + Box), "node_id", node);
        Assert.Equal(Lng, before.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble(), 6);

        await PutAsync(client, $"{Cabinets}/{cabinetId}", CabinetReplacement("simulated", "POINT (108.85 12.85)"));

        var after = Find(await GetAsync(client, MapNodes + Box), "node_id", node);
        Assert.Equal(108.85, after.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble(), 6);
        Assert.Equal(12.85, after.GetProperty("geometry").GetProperty("coordinates")[1].GetDouble(), 6);

        var cabinetFeature = Find(await GetAsync(client, MapCabinets + Box), "cabinet_id", cabinetId);
        Assert.Equal(node, cabinetFeature.GetProperty("properties").GetProperty("iot_node_id").GetString());
    }

    // ── Import (CAB-7) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cabinets_import_idempotently_and_a_feeders_file_attaches_to_them_by_code()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var cabinets = "external_ref,cabinet_name,commune_id,geom_wkt,data_source"
            + $"\n{tag}-C1,Tủ A,{fixture.CommuneId},POINT(108.8 12.8),field";

        var first = await AssetImportTests.ImportAsync(client, "cabinets", "c.csv", cabinets);
        Assert.Equal(1, first.GetProperty("inserted").GetInt32());

        var again = await AssetImportTests.ImportAsync(client, "cabinets", "c.csv", cabinets);
        Assert.Equal(0, again.GetProperty("inserted").GetInt32());
        Assert.Equal(1, again.GetProperty("unchanged").GetInt32());

        var feeders = "external_ref,feeder_name,commune_id,cabinet_external_ref"
            + $"\n{tag}-F1,Lộ 1,{fixture.CommuneId},{tag}-C1";
        Assert.Equal(1, (await AssetImportTests.ImportAsync(client, "feeders", "f.csv", feeders)).GetProperty("inserted").GetInt32());

        var cabinetId = await CabinetByRefAsync($"{tag}-C1");
        Assert.Equal(cabinetId, await FeederCabinetAsync(await FeederByRefAsync($"{tag}-F1")));

        // A blank cell — or no column at all — KEEPS the cabinet: the import never detaches one.
        await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id,cabinet_external_ref" + $"\n{tag}-F1,Lộ 1 mới,{fixture.CommuneId},");
        await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id" + $"\n{tag}-F1,Lộ 1 mới hơn,{fixture.CommuneId}");
        Assert.Equal(cabinetId, await FeederCabinetAsync(await FeederByRefAsync($"{tag}-F1")));
    }

    [Fact]
    public async Task Import_refuses_per_row_what_the_database_would_refuse_at_the_write()
    {
        var client = await fixture.BothCommunesClientAsync();
        var tag = Tag();
        await AssetImportTests.ImportAsync(client, "cabinets", "c.csv",
            "external_ref,cabinet_name,commune_id,geom_wkt,data_source"
            + $"\n{tag}-HOME,Tủ nhà,{fixture.CommuneId},POINT(108.8 12.8),simulated"
            + $"\n{tag}-AWAY,Tủ xã khác,{fixture.ForeignCommuneId},POINT(108.8 12.8),simulated"
            + $"\n{tag}-OTHER,Tủ thứ hai,{fixture.CommuneId},POINT(108.8 12.8),simulated");
        await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id,cabinet_external_ref" + $"\n{tag}-SW,Lộ có rơ-le,{fixture.CommuneId},{tag}-HOME");

        var home = await CabinetByRefAsync($"{tag}-HOME");
        var switched = await FeederByRefAsync($"{tag}-SW");
        await ControlAsync(switched, await NewNodeAsync(home), home);

        var feeders = await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id,cabinet_external_ref"
            + $"\n{tag}-X,Lộ chéo xã,{fixture.CommuneId},{tag}-AWAY"
            + $"\n{tag}-SW,Lộ có rơ-le,{fixture.CommuneId},{tag}-OTHER");
        Assert.Equal(0, feeders.GetProperty("inserted").GetInt32() + feeders.GetProperty("updated").GetInt32());
        Assert.Equal([2, 3], RowNumbers(feeders, "cabinet_external_ref"));

    }

    // ── Database rules, proven with RAW SQL (CAB-3, CAB-4) ────────────────────────────────────────

    /// <summary>
    /// 🔴 The EF model does not know these four constraints (raw SQL in the migration), so no later migration will
    /// notice one going missing. This is the only thing that would.
    /// </summary>
    [Fact]
    public async Task Every_database_only_cabinet_constraint_exists()
    {
        string[] names =
        [
            CabinetConstraints.FeederCabinetKey, CabinetConstraints.NodeCabinetKey,
            CabinetConstraints.RelayFeederSameCabinet, CabinetConstraints.RelayNodeSameCabinet,
        ];

        var present = await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT conname::text AS \"Value\" FROM pg_constraint WHERE conname = ANY({names})")
            .ToListAsync());

        Assert.Equal([.. names.Order(StringComparer.Ordinal)], [.. present.Order(StringComparer.Ordinal)]);

        // CAB-5's three, gone with migration DropFieldCabinetRule — and the column that fed them.
        string[] dropped = ["fk_iot_node_cabinet_data_source", "ux_electrical_cabinet_cabinet_id_data_source", "ck_iot_node_cabinet_not_field"];
        Assert.Empty(await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT conname::text AS \"Value\" FROM pg_constraint WHERE conname = ANY({dropped})")
            .ToListAsync()));
        Assert.Empty(await fixture.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT column_name::text AS \"Value\" FROM information_schema.columns WHERE table_name = 'iot_node' AND column_name = 'cabinet_data_source'")
            .ToListAsync()));
    }

    [Fact]
    public async Task A_relay_cannot_switch_a_feeder_of_another_cabinet_even_in_raw_sql()
    {
        var cabinetA = await NewCabinetAsync(fixture.CommuneId);
        var cabinetB = await NewCabinetAsync(fixture.CommuneId);
        var node = await NewNodeAsync(cabinetA);
        var feederOfB = await NewFeederAsync(fixture.CommuneId, cabinetB);
        var loose = await NewFeederAsync(fixture.CommuneId, null);

        // The relay names the device's cabinet: the feeder is in another one, or in none.
        foreach (var feeder in new[] { feederOfB, loose })
        {
            var error = await SqlFailsAsync(
                $"INSERT INTO feeder_control (feeder_id, node_id, commune_id, cabinet_id, relay_no) VALUES ('{feeder}', '{node}', '{fixture.CommuneId}', '{cabinetA}', 1)");
            Assert.Equal(CabinetConstraints.RelayFeederSameCabinet, error.ConstraintName);
        }

        // The relay names the feeder's cabinet: the device is in another one.
        var other = await SqlFailsAsync(
            $"INSERT INTO feeder_control (feeder_id, node_id, commune_id, cabinet_id, relay_no) VALUES ('{feederOfB}', '{node}', '{fixture.CommuneId}', '{cabinetB}', 1)");
        Assert.Equal(CabinetConstraints.RelayNodeSameCabinet, other.ConstraintName);
    }

    [Fact]
    public async Task A_switched_feeder_cannot_be_moved_or_detached_in_raw_sql()
    {
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        var other = await NewCabinetAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId, cabinetId);
        await ControlAsync(feederId, await NewNodeAsync(cabinetId), cabinetId);

        foreach (var target in new[] { "NULL", $"'{other}'" })
        {
            var error = await SqlFailsAsync($"UPDATE feeder SET cabinet_id = {target} WHERE feeder_id = '{feederId}'");
            Assert.Equal(CabinetConstraints.RelayFeederSameCabinet, error.ConstraintName);
        }
    }

    [Fact]
    public async Task A_cabinet_carries_at_most_one_device()
    {
        var cabinetId = await NewCabinetAsync(fixture.CommuneId);
        await NewNodeAsync(cabinetId);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => NewNodeAsync(cabinetId));

        Assert.Equal("ux_iot_node_cabinet_id", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => $"CAB{Guid.NewGuid():N}"[..11].ToUpperInvariant();

    private static object CabinetBody(string communeId, string? externalRef = null) => new
    {
        external_ref = externalRef,
        cabinet_name = "cabinet probe",
        commune_id = communeId,
        geom_wkt = $"POINT ({Lng} {Lat})",
        data_source = "field",
    };

    private static object CabinetReplacement(string dataSource, string geom = "POINT (108.8 12.8)") => new
    {
        cabinet_name = "cabinet probe, replaced",
        geom_wkt = geom,
        data_source = dataSource,
    };

    private static async Task<string> CreateCabinetAsync(HttpClient client, string communeId, string? externalRef = null)
    {
        var response = await client.PostAsJsonAsync(Cabinets, CabinetBody(communeId, externalRef));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response.Headers.Location!.OriginalString.Split('/')[^1];
    }

    private async Task<string> CreateFeederAsync(HttpClient client, string? cabinetId)
    {
        var response = await client.PostAsJsonAsync(
            Feeders, new { feeder_name = "cabinet probe feeder", commune_id = fixture.CommuneId, cabinet_id = cabinetId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response.Headers.Location!.OriginalString.Split('/')[^1];
    }

    private static async Task PutAsync(HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Walks the pages — this fixture's database is shared with every other asset test.</summary>
    private static async Task<JsonElement> FindAsync(HttpClient client, string url, string key, string id)
    {
        for (var page = 1; ; page++)
        {
            var items = (await GetAsync(client, $"{url}&page={page}")).GetProperty("items").EnumerateArray().ToList();

            var match = items.FirstOrDefault(item => item.GetProperty(key).GetString() == id);
            if (match.ValueKind == JsonValueKind.Object)
            {
                return match.Clone();
            }

            Assert.True(items.Count > 0, $"{id} was not on any page of {url}.");
        }
    }

    private static JsonElement Find(JsonElement collection, string key, string id)
    {
        var match = collection.GetProperty("features").EnumerateArray()
            .FirstOrDefault(feature => feature.GetProperty("properties").GetProperty(key).GetString() == id);
        Assert.True(match.ValueKind == JsonValueKind.Object, $"{id} was not in the collection.");
        return match;
    }

    private static IEnumerable<string?> Ids(JsonElement collection)
        => collection.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty("cabinet_id").GetString());

    private static string[] Keys(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString() ?? "<null>")];

    private static int[] RowNumbers(JsonElement result, string column)
        => [.. result.GetProperty("rows").EnumerateArray()
            .Where(row => row.GetProperty("column").GetString() == column)
            .Select(row => row.GetProperty("row").GetInt32())
            .Order()];

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").Clone();

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await ErrorAsync(response)).GetProperty("code").GetString();

    private Task<string?> FeederCabinetAsync(string feederId)
        => fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(feeder => feeder.FeederId == feederId).Select(feeder => feeder.CabinetId).SingleAsync());

    private Task<string> FeederByRefAsync(string externalRef)
        => fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(feeder => feeder.ExternalRef == externalRef).Select(feeder => feeder.FeederId).SingleAsync());

    private Task<string> CabinetByRefAsync(string externalRef)
        => fixture.QueryAsync(db => db.Set<ElectricalCabinet>().IgnoreQueryFilters()
            .Where(cabinet => cabinet.ExternalRef == externalRef).Select(cabinet => cabinet.CabinetId).SingleAsync());

    /// <summary>
    /// A pair like <c>FDR-999999</c> / <c>FDR-1000000</c> that no row holds, far above where <c>feeder_id_seq</c>
    /// stands — chosen from the LIVE table, never a literal (CLAUDE.md: the shared sequence).
    /// </summary>
    private async Task<(string Shorter, string Longer)> FreeStraddlingFeederIdsAsync()
    {
        for (var nines = 6; nines <= 12; nines++)
        {
            var shorter = "FDR-" + new string('9', nines);
            var longer = "FDR-1" + new string('0', nines);
            var taken = await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
                .AnyAsync(feeder => feeder.FeederId == shorter || feeder.FeederId == longer));
            if (!taken)
            {
                return (shorter, longer);
            }
        }

        throw new InvalidOperationException("No free straddling pair of feeder ids between 6 and 12 digits.");
    }

    /// <summary>Runs one raw statement that must fail, and returns PostgreSQL's refusal.</summary>
    private async Task<PostgresException> SqlFailsAsync(string sql)
        => await Assert.ThrowsAsync<PostgresException>(() => fixture.QueryAsync(db => db.Database.ExecuteSqlRawAsync(sql)));

    private Task<T> AsSystemAsync<T>(Func<LuxMapDbContext, Task<T>> write)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                return await write(db);
            }
        });

    private Task<string> NewCabinetAsync(string communeId, DataSource source = DataSource.Simulated)
        => AsSystemAsync(async db =>
        {
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "cabinet probe",
                CommuneId = communeId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = source,
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            return cabinet.CabinetId;
        });

    private Task<string> NewFeederAsync(string communeId, string? cabinetId)
        => AsSystemAsync(async db =>
        {
            var feeder = new Feeder { FeederName = "cabinet probe feeder", CommuneId = communeId, CabinetId = cabinetId };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

    /// <summary>A device in <paramref name="cabinetId"/>, in the cabinet's commune.</summary>
    private Task<string> NewNodeAsync(string cabinetId, DataSource source = DataSource.Simulated)
        => AsSystemAsync(async db =>
        {
            var cabinet = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().SingleAsync(candidate => candidate.CabinetId == cabinetId);
            var node = new IotNode
            {
                CommuneId = cabinet.CommuneId,
                CabinetId = cabinetId,
                DataSource = source,
            };
            db.Add(node);
            await db.SaveChangesAsync();
            return node.NodeId;
        });

    private Task<int> ControlAsync(string feederId, string nodeId, string cabinetId)
        => AsSystemAsync(db =>
        {
            db.Add(new FeederControl
            {
                FeederId = feederId,
                NodeId = nodeId,
                CommuneId = fixture.CommuneId,
                CabinetId = cabinetId,
                RelayNo = 1,
            });
            return db.SaveChangesAsync();
        });
}
