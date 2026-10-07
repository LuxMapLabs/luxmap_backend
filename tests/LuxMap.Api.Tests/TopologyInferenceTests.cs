using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Crud;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// TOPO-INFER — provenance labels on pole → feeder and feeder → cabinet, and a cabinet's branch diagram (drift TI-1…TI-6).
/// </summary>
/// <remarks>
/// ⚠️ SELF-SIGNED shape. Expectations are LITERALS. The rule under test is TI-2: absent label + same relation → KEEP; moved or
/// new relation → <c>inferred</c>; no relation → no label; <c>null</c> / wrong type / label without relation → 400. Nothing
/// is ever <c>verified</c> unless a caller says so.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class TopologyInferenceTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/assets/poles";
    private const string Feeders = "/api/v1/assets/feeders";

    /// <summary>A box of its own, away from every other map test.</summary>
    private const double Lng = 109.2;
    private const double Lat = 13.2;

    // ── Pole → feeder: the TI-2 table ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_relation_is_inferred_unless_the_caller_says_verified()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var feeder = await NewFeederAsync(null);

        var plain = await CreatePoleAsync(client, segment, new { feeder_id = feeder });
        var verified = await CreatePoleAsync(client, segment, new { feeder_id = feeder, feeder_source = "verified" });
        var bare = await CreatePoleAsync(client, segment, new { });

        Assert.Equal((feeder, TopologySource.Inferred), await RelationAsync(plain));
        Assert.Equal((feeder, TopologySource.Verified), await RelationAsync(verified));
        Assert.Equal(((string?)null, (TopologySource?)null), await RelationAsync(bare));

        var row = (await GetAsync(client, $"{Poles}/{verified}")).GetProperty("pole");
        Assert.Equal("verified", row.GetProperty("feeder_source").GetString());
    }

    [Theory]
    [InlineData("""{ "feeder_source": "verified" }""")]                  // a label with no relation
    [InlineData("""{ "feeder_id": "FEEDER", "feeder_source": null }""")]  // null while the relation stays
    [InlineData("""{ "feeder_id": "FEEDER", "feeder_source": 1 }""")]     // wrong type
    [InlineData("""{ "feeder_id": "FEEDER", "feeder_source": "maybe" }""")]
    public async Task A_label_that_records_nothing_or_nothing_known_is_a_400(string extra)
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var feeder = await NewFeederAsync(null);

        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extra.Replace("FEEDER", feeder))!;
        body["segment_id"] = JsonSerializer.SerializeToElement(segment);
        body["commune_id"] = JsonSerializer.SerializeToElement(fixture.CommuneId);
        body["geom_wkt"] = JsonSerializer.SerializeToElement($"POINT ({Lng} {Lat})");
        body["data_source"] = JsonSerializer.SerializeToElement("field");

        var response = await client.PostAsJsonAsync(Poles, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("feeder_source", (await ErrorAsync(response)).GetProperty("details").GetProperty("field").GetString());
    }

    /// <summary>
    /// 🔴 Absent label + same feeder KEEPS <c>verified</c>: a form written before labels existed must not downgrade a verified
    /// relation every time it saves the pole. Moving the pole is a new claim and goes back to <c>inferred</c>.
    /// </summary>
    [Fact]
    public async Task A_full_replacement_keeps_the_label_while_the_feeder_stays_and_resets_it_when_it_moves()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var first = await NewFeederAsync(null);
        var second = await NewFeederAsync(null);
        var pole = await CreatePoleAsync(client, segment, new { feeder_id = first, feeder_source = "verified" });

        await PutAsync(client, $"{Poles}/{pole}", ReplacePole(segment, new { feeder_id = first }));
        Assert.Equal((first, TopologySource.Verified), await RelationAsync(pole));

        await PutAsync(client, $"{Poles}/{pole}", ReplacePole(segment, new { feeder_id = second }));
        Assert.Equal((second, TopologySource.Inferred), await RelationAsync(pole));

        // Full replacement without feeder_id clears the circuit (BE-12a) — and the label with it.
        await PutAsync(client, $"{Poles}/{pole}", ReplacePole(segment, new { }));
        Assert.Equal(((string?)null, (TopologySource?)null), await RelationAsync(pole));
    }

    [Fact]
    public async Task The_narrow_feeder_endpoint_follows_the_same_rule()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var first = await NewFeederAsync(null);
        var second = await NewFeederAsync(null);
        var pole = await CreatePoleAsync(client, segment, new { });
        var url = $"{Poles}/{pole}/feeder";

        await PutAsync(client, url, new { feeder_id = first, feeder_source = "verified" });
        Assert.Equal((first, TopologySource.Verified), await RelationAsync(pole));

        await PutAsync(client, url, new { feeder_id = first });
        Assert.Equal((first, TopologySource.Verified), await RelationAsync(pole));

        await PutAsync(client, url, new { feeder_id = second });
        Assert.Equal((second, TopologySource.Inferred), await RelationAsync(pole));

        var refused = await client.PutAsJsonAsync(url, new { feeder_id = (string?)null, feeder_source = "verified" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await PutAsync(client, url, new { feeder_id = (string?)null });
        Assert.Equal(((string?)null, (TopologySource?)null), await RelationAsync(pole));
    }

    // ── Feeder → cabinet (D-10) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_feeder_carries_the_provenance_of_its_cabinet_and_the_label_can_be_set_on_the_kept_cabinet()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync();
        var other = await NewCabinetAsync();

        var created = await client.PostAsJsonAsync(
            Feeders, new { feeder_name = "topology probe", commune_id = fixture.CommuneId, cabinet_id = cabinet });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var feeder = created.Headers.Location!.OriginalString.Split('/')[^1];
        Assert.Equal((cabinet, TopologySource.Inferred), await CabinetRelationAsync(feeder));

        // cabinet_id absent keeps the cabinet (CAB-6); the label applies to the kept one.
        await PutAsync(client, $"{Feeders}/{feeder}", new { feeder_name = "verified", cabinet_source = "verified" });
        Assert.Equal((cabinet, TopologySource.Verified), await CabinetRelationAsync(feeder));

        var row = (await GetAsync(client, $"{Feeders}/{feeder}")).GetProperty("feeder").GetProperty("cabinet");
        Assert.Equal("verified", row.GetProperty("cabinet_source").GetString());

        await PutAsync(client, $"{Feeders}/{feeder}", new { feeder_name = "moved", cabinet_id = other });
        Assert.Equal((other, TopologySource.Inferred), await CabinetRelationAsync(feeder));

        await PutAsync(client, $"{Feeders}/{feeder}", new { feeder_name = "detached", cabinet_id = (string?)null });
        Assert.Equal(((string?)null, (TopologySource?)null), await CabinetRelationAsync(feeder));
    }

    [Theory]
    [InlineData("""{ "cabinet_id": null, "cabinet_source": "verified" }""")] // a label with no cabinet
    [InlineData("""{ "cabinet_id": "CABINET", "cabinet_source": null }""")]  // null while the cabinet stays
    [InlineData("""{ "cabinet_id": "CABINET", "cabinet_source": ["verified"] }""")]
    public async Task A_feeder_label_that_records_nothing_is_a_400_and_writes_nothing(string extra)
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync();
        var feeder = await NewFeederAsync(cabinet, TopologySource.Verified);

        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extra.Replace("CABINET", cabinet))!;
        body["feeder_name"] = JsonSerializer.SerializeToElement("refused");

        var created = await client.PostAsJsonAsync(
            Feeders, new Dictionary<string, JsonElement>(body) { ["commune_id"] = JsonSerializer.SerializeToElement(fixture.CommuneId) });
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal("cabinet_source", (await ErrorAsync(created)).GetProperty("details").GetProperty("field").GetString());

        var replaced = await client.PutAsJsonAsync($"{Feeders}/{feeder}", body);
        Assert.Equal(HttpStatusCode.BadRequest, replaced.StatusCode);
        Assert.Equal((cabinet, TopologySource.Verified), await CabinetRelationAsync(feeder));
    }

    [Fact]
    public async Task A_feeder_import_refuses_an_unknown_label_and_a_label_without_a_cabinet_per_row()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"TC{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await AssetImportTests.ImportAsync(client, "cabinets", "c.csv",
            "external_ref,cabinet_name,commune_id,geom_wkt,data_source" + $"\n{tag}-C,Tủ,{fixture.CommuneId},POINT({Lng} {Lat}),field");

        var result = await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id,cabinet_external_ref,cabinet_source"
            + $"\n{tag}-F1,Lộ 1,{fixture.CommuneId},{tag}-C,maybe"
            + $"\n{tag}-F2,Lộ 2,{fixture.CommuneId},,verified"
            + $"\n{tag}-F3,Lộ 3,{fixture.CommuneId},{tag}-C,verified");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(
            [(2, "cabinet_source"), (3, "cabinet_source")],
            result.GetProperty("rows").EnumerateArray()
                .Select(row => (row.GetProperty("row").GetInt32(), row.GetProperty("column").GetString() ?? "<null>")).ToArray());

        var verified = await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(f => f.ExternalRef == $"{tag}-F3").Select(f => f.FeederId).SingleAsync());
        Assert.Equal(TopologySource.Verified, (await CabinetRelationAsync(verified)).Source);
    }

    // ── Import ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_keeps_a_label_on_a_blank_cell_resets_it_on_a_move_and_refuses_an_unknown_one_per_row()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"TI{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        await AssetImportTests.ImportAsync(client, "segments", "s.csv",
            "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source"
            + $"\n{tag}-S,Topology road,inter_village,100,\"LINESTRING({Lng} {Lat}, {Lng + 0.01} {Lat})\",{fixture.CommuneId},field");
        await AssetImportTests.ImportAsync(client, "feeders", "f.csv",
            "external_ref,feeder_name,commune_id" + $"\n{tag}-F1,Lộ 1,{fixture.CommuneId}\n{tag}-F2,Lộ 2,{fixture.CommuneId}");

        const string Header = "external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,data_source,feeder_source";
        string Row(string feeder, string label) => $"\n{tag}-P,{tag}-S,{feeder},{fixture.CommuneId},POINT({Lng} {Lat}),field,{label}";

        await AssetImportTests.ImportAsync(client, "poles", "p.csv", Header + Row($"{tag}-F1", "verified"));
        var pole = await PoleByRefAsync($"{tag}-P");
        Assert.Equal(TopologySource.Verified, (await RelationAsync(pole)).Source);

        var same = await AssetImportTests.ImportAsync(client, "poles", "p.csv", Header + Row($"{tag}-F1", ""));
        Assert.Equal(1, same.GetProperty("unchanged").GetInt32());
        Assert.Equal(TopologySource.Verified, (await RelationAsync(pole)).Source);

        await AssetImportTests.ImportAsync(client, "poles", "p.csv", Header + Row($"{tag}-F2", ""));
        Assert.Equal(TopologySource.Inferred, (await RelationAsync(pole)).Source);

        var bad = await AssetImportTests.ImportAsync(client, "poles", "p.csv", Header + Row($"{tag}-F2", "maybe"));
        Assert.Equal(1, bad.GetProperty("failed").GetInt32());
        Assert.Equal("feeder_source", bad.GetProperty("rows")[0].GetProperty("column").GetString());

        var orphan = await AssetImportTests.ImportAsync(client, "poles", "p.csv", Header + Row("", "verified"));
        Assert.Equal(1, orphan.GetProperty("failed").GetInt32());
    }

    /// <summary>A GeoJSON object where a label belongs is a row error — it must not be read as an empty cell, i.e. "keep".</summary>
    [Fact]
    public async Task A_geojson_label_that_is_not_a_scalar_fails_the_row()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId, externalRef: $"TIG{Guid.NewGuid():N}"[..10]);
        var feeder = await NewFeederAsync(null, externalRef: $"TIF{Guid.NewGuid():N}"[..10]);
        var (segmentRef, feederRef) = await fixture.QueryAsync(async db => (
            await db.Set<RoadSegment>().IgnoreQueryFilters().Where(s => s.SegmentId == segment).Select(s => s.ExternalRef).SingleAsync(),
            await db.Set<Feeder>().IgnoreQueryFilters().Where(f => f.FeederId == feeder).Select(f => f.ExternalRef).SingleAsync()));

        var json = JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { Lng, Lat } },
                    properties = new Dictionary<string, object>
                    {
                        ["external_ref"] = $"TIP{Guid.NewGuid():N}"[..10],
                        ["segment_external_ref"] = segmentRef!,
                        ["feeder_external_ref"] = feederRef!,
                        ["commune_id"] = fixture.CommuneId,
                        ["data_source"] = "field",
                        ["feeder_source"] = new { value = "verified" },
                    },
                },
            },
        });

        var result = await AssetImportTests.ImportAsync(client, "poles", "p.geojson", json);

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal("feeder_source", result.GetProperty("rows")[0].GetProperty("column").GetString());
    }

    // ── Pair write and the database rule ───────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 Two writers interleave: A verifies feeder F1, B moves the pole to F2. Written column by column the database would end
    /// at <c>(F2, verified)</c> — a pair nobody asserted. Written as a pair, the last writer wins the WHOLE pair.
    /// </summary>
    [Fact]
    public async Task Interleaved_writers_never_leave_a_pair_nobody_asserted()
    {
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var first = await NewFeederAsync(null);
        var second = await NewFeederAsync(null);
        var pole = await NewPoleAsync(segment, first, TopologySource.Inferred);

        await fixture.QueryAsync(async a =>
        {
            await fixture.QueryAsync(async b =>
            {
                using (a.EnterUnscopedSystemWriteBackdoor())
                using (b.EnterUnscopedSystemWriteBackdoor())
                {
                    var seenByA = await a.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
                    var seenByB = await b.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);

                    TopologyLink.ApplyPoleFeeder(a, seenByA, first, TopologySource.Verified);
                    TopologyLink.ApplyPoleFeeder(b, seenByB, second, TopologySource.Inferred);

                    await a.SaveChangesAsync();
                    await b.SaveChangesAsync();
                }

                return 0;
            });

            return 0;
        });

        Assert.Equal((second, TopologySource.Inferred), await RelationAsync(pole));
    }

    [Fact]
    public async Task The_database_refuses_a_relation_without_a_label_and_a_label_without_a_relation()
    {
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var feeder = await NewFeederAsync(null);
        var pole = await NewPoleAsync(segment, null, null);
        var cabinet = await NewCabinetAsync();

        foreach (var sql in new[]
                 {
                     $"UPDATE pole SET feeder_id = '{feeder}' WHERE pole_id = '{pole}'",
                     $"UPDATE pole SET feeder_source = 'verified' WHERE pole_id = '{pole}'",
                 })
        {
            Assert.Equal("ck_pole_feeder_source_matches_feeder", (await SqlFailsAsync(sql)).ConstraintName);
        }

        foreach (var sql in new[]
                 {
                     $"UPDATE feeder SET cabinet_id = '{cabinet}' WHERE feeder_id = '{feeder}'",
                     $"UPDATE feeder SET cabinet_source = 'inferred' WHERE feeder_id = '{feeder}'",
                 })
        {
            Assert.Equal("ck_feeder_cabinet_source_matches_cabinet", (await SqlFailsAsync(sql)).ConstraintName);
        }
    }

    // ── GET /map/cabinets/{id}/topology (TI-4) ─────────────────────────────────────────────────

    /// <summary>
    /// A cabinet in the middle of a road: poles before it form a branch walked toward the start, poles after it a branch walked
    /// toward the end. Edge 1 of each branch starts at the cabinet.
    /// </summary>
    [Fact]
    public async Task The_diagram_branches_on_both_sides_of_the_cabinet_in_road_order()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var cabinet = await NewCabinetAsync(Lng + 0.005, Lat + 0.0005);
        var feeder = await NewFeederAsync(cabinet);
        var near0 = await NewPoleAsync(segment, feeder, TopologySource.Inferred, Lng + 0.003);
        var far0 = await NewPoleAsync(segment, feeder, TopologySource.Inferred, Lng + 0.001);
        var near1 = await NewPoleAsync(segment, feeder, TopologySource.Inferred, Lng + 0.007);
        var far1 = await NewPoleAsync(segment, feeder, TopologySource.Inferred, Lng + 0.009);

        var features = Features(await GetAsync(client, Topology(cabinet)));

        Assert.Equal(
            [(1, 1, cabinet, near0), (1, 2, near0, far0), (2, 1, cabinet, near1), (2, 2, near1, far1)],
            features.Select(Edge).ToArray());

        var first = features[0];
        Assert.False(first.TryGetProperty("id", out _));
        Assert.Equal("LineString", first.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(Lng + 0.005, first.GetProperty("geometry").GetProperty("coordinates")[0][0].GetDouble(), 6);
        Assert.Equal(
            ["branch", "feeder_id", "feeder_source", "from_id", "order", "segment_id", "to_pole_id"],
            first.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Edge 1 takes the LOWER of the feeder → cabinet and the pole → feeder labels; later edges the arriving pole's.</summary>
    [Fact]
    public async Task An_edge_is_only_as_verified_as_its_weakest_relation()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var cabinet = await NewCabinetAsync(Lng - 0.001, Lat);
        var feeder = await NewFeederAsync(cabinet, TopologySource.Inferred);
        await NewPoleAsync(segment, feeder, TopologySource.Verified, Lng + 0.001);
        await NewPoleAsync(segment, feeder, TopologySource.Verified, Lng + 0.002);
        await NewPoleAsync(segment, feeder, TopologySource.Inferred, Lng + 0.003);

        var labels = Features(await GetAsync(client, Topology(cabinet)))
            .Select(f => f.GetProperty("properties").GetProperty("feeder_source").GetString() ?? "<null>").ToArray();

        Assert.Equal(["inferred", "verified", "inferred"], labels);

        await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE feeder SET cabinet_source = 'verified' WHERE feeder_id = {feeder}"));
        var after = Features(await GetAsync(client, Topology(cabinet)))
            .Select(f => f.GetProperty("properties").GetProperty("feeder_source").GetString() ?? "<null>").ToArray();
        Assert.Equal(["verified", "verified", "inferred"], after);
    }

    /// <summary>
    /// 🔴 The road may belong to ANOTHER commune (<c>inter_commune</c>): its geometry is read unfiltered for ordering, or the
    /// lookup would miss it. Poles at the same point tie-break by id length first — as text the longer id would win.
    /// </summary>
    [Fact]
    public async Task A_foreign_road_still_orders_the_branch_and_ties_follow_the_id_rule()
    {
        var client = await fixture.ManagerClientAsync();
        var foreignRoad = await NewSegmentAsync(fixture.ForeignCommuneId);
        var cabinet = await NewCabinetAsync(Lng - 0.001, Lat);
        var feeder = await NewFeederAsync(cabinet);
        var (shorter, longer) = await FreeStraddlingPoleIdsAsync();

        try
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO pole (pole_id, segment_id, feeder_id, feeder_source, commune_id, geom, data_source) VALUES
                ({longer}, {foreignRoad}, {feeder}, 'inferred', {fixture.CommuneId}, ST_SetSRID(ST_MakePoint(109.205, 13.2), 4326), 'field'),
                ({shorter}, {foreignRoad}, {feeder}, 'inferred', {fixture.CommuneId}, ST_SetSRID(ST_MakePoint(109.205, 13.2), 4326), 'field')
                """));

            var edges = Features(await GetAsync(client, Topology(cabinet))).Select(Edge).ToArray();

            Assert.Equal([(1, 1, cabinet, shorter), (1, 2, shorter, longer)], edges);
        }
        finally
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"DELETE FROM pole WHERE pole_id IN ({shorter}, {longer})"));
        }
    }

    /// <summary>
    /// Branches are numbered across the whole collection in feeder order — and feeders written in ONE statement share
    /// <c>created_at</c>, so the id-length tiebreaker decides: as text <c>FDR-10000000</c> would come first.
    /// </summary>
    [Fact]
    public async Task Branches_follow_feeder_order_across_a_width_boundary()
    {
        var client = await fixture.ManagerClientAsync();
        var segment = await NewSegmentAsync(fixture.CommuneId);
        var cabinet = await NewCabinetAsync(Lng - 0.001, Lat);
        var (shorter, longer) = await FreeStraddlingFeederIdsAsync();

        try
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO feeder (feeder_id, feeder_name, commune_id, cabinet_id, cabinet_source) VALUES
                ({longer}, 'order probe', {fixture.CommuneId}, {cabinet}, 'inferred'),
                ({shorter}, 'order probe', {fixture.CommuneId}, {cabinet}, 'inferred')
                """));
            var onLonger = await NewPoleAsync(segment, longer, TopologySource.Inferred, Lng + 0.001);
            var onShorter = await NewPoleAsync(segment, shorter, TopologySource.Inferred, Lng + 0.002);

            var edges = Features(await GetAsync(client, Topology(cabinet)))
                .Select(f => (f.GetProperty("properties").GetProperty("branch").GetInt32(),
                    f.GetProperty("properties").GetProperty("feeder_id").GetString() ?? "<null>",
                    f.GetProperty("properties").GetProperty("to_pole_id").GetString() ?? "<null>"))
                .ToArray();

            Assert.Equal([(1, shorter, onShorter), (2, longer, onLonger)], edges);
        }
        finally
        {
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"DELETE FROM pole WHERE feeder_id IN ({shorter}, {longer})"));
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"DELETE FROM feeder WHERE feeder_id IN ({shorter}, {longer})"));
        }
    }

    [Fact]
    public async Task No_relation_is_an_empty_diagram_and_a_foreign_cabinet_is_a_404()
    {
        var client = await fixture.ManagerClientAsync();
        var lonely = await NewCabinetAsync();
        var foreign = await NewCabinetAsync(communeId: fixture.ForeignCommuneId);

        Assert.Empty(Features(await GetAsync(client, Topology(lonely))));

        var response = await client.GetAsync(Topology(foreign));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ASSET_NOT_FOUND", (await ErrorAsync(response)).GetProperty("code").GetString());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static string Topology(string cabinetId) => $"/api/v1/map/cabinets/{cabinetId}/topology";

    private static JsonElement[] Features(JsonElement collection) => [.. collection.GetProperty("features").EnumerateArray()];

    private static (int Branch, int Order, string From, string To) Edge(JsonElement feature)
    {
        var p = feature.GetProperty("properties");
        return (p.GetProperty("branch").GetInt32(), p.GetProperty("order").GetInt32(),
            p.GetProperty("from_id").GetString()!, p.GetProperty("to_pole_id").GetString()!);
    }

    private object ReplacePole(string segment, object relation)
    {
        var body = JsonSerializer.SerializeToNode(relation)!.AsObject();
        body["segment_id"] = segment;
        body["geom_wkt"] = $"POINT ({Lng} {Lat})";
        body["data_source"] = "field";
        return body;
    }

    private async Task<string> CreatePoleAsync(HttpClient client, string segment, object relation)
    {
        var body = JsonSerializer.SerializeToNode(relation)!.AsObject();
        body["segment_id"] = segment;
        body["commune_id"] = fixture.CommuneId;
        body["geom_wkt"] = $"POINT ({Lng} {Lat})";
        body["data_source"] = "field";

        var response = await client.PostAsJsonAsync(Poles, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").Clone();

    private async Task<PostgresException> SqlFailsAsync(string sql)
        => await Assert.ThrowsAsync<PostgresException>(() => fixture.QueryAsync(db => db.Database.ExecuteSqlRawAsync(sql)));

    private Task<(string? Feeder, TopologySource? Source)> RelationAsync(string poleId)
        => fixture.QueryAsync(async db => await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == poleId)
            .Select(p => new ValueTuple<string?, TopologySource?>(p.FeederId, p.FeederSource)).SingleAsync());

    private Task<(string? Cabinet, TopologySource? Source)> CabinetRelationAsync(string feederId)
        => fixture.QueryAsync(async db => await db.Set<Feeder>().IgnoreQueryFilters().Where(f => f.FeederId == feederId)
            .Select(f => new ValueTuple<string?, TopologySource?>(f.CabinetId, f.CabinetSource)).SingleAsync());

    private Task<string> PoleByRefAsync(string externalRef)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.ExternalRef == externalRef).Select(p => p.PoleId).SingleAsync());

    /// <summary>A free pair like <c>POLE-99999999</c> / <c>POLE-100000000</c>, chosen from the live table, never a literal.</summary>
    private async Task<(string Shorter, string Longer)> FreeStraddlingPoleIdsAsync()
    {
        for (var nines = 8; nines <= 14; nines++)
        {
            var shorter = "POLE-" + new string('9', nines);
            var longer = "POLE-1" + new string('0', nines);
            if (!await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
                    .AnyAsync(p => p.PoleId == shorter || p.PoleId == longer)))
            {
                return (shorter, longer);
            }
        }

        throw new InvalidOperationException("No free straddling pair of pole ids.");
    }

    /// <summary>A free pair like <c>FDR-9999999</c> / <c>FDR-10000000</c>, chosen from the live table.</summary>
    private async Task<(string Shorter, string Longer)> FreeStraddlingFeederIdsAsync()
    {
        for (var nines = 7; nines <= 14; nines++)
        {
            var shorter = "FDR-" + new string('9', nines);
            var longer = "FDR-1" + new string('0', nines);
            if (!await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
                    .AnyAsync(f => f.FeederId == shorter || f.FeederId == longer)))
            {
                return (shorter, longer);
            }
        }

        throw new InvalidOperationException("No free straddling pair of feeder ids.");
    }

    private Task<T> AsSystemAsync<T>(Func<LuxMapDbContext, Task<T>> write)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                return await write(db);
            }
        });

    private Task<string> NewSegmentAsync(string communeId, string? externalRef = null)
        => AsSystemAsync(async db =>
        {
            var segment = new RoadSegment
            {
                SegmentName = "topology probe road",
                RoadClass = RoadClass.InterCommune,
                LengthM = 1000,
                Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat)]) { SRID = 4326 },
                CommuneId = communeId,
                DataSource = DataSource.Field,
                ExternalRef = externalRef,
            };
            db.Add(segment);
            await db.SaveChangesAsync();
            return segment.SegmentId;
        });

    private Task<string> NewCabinetAsync(double lng = Lng, double lat = Lat, string? communeId = null)
        => AsSystemAsync(async db =>
        {
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "topology probe cabinet",
                CommuneId = communeId ?? fixture.CommuneId,
                Geom = new Point(lng, lat) { SRID = 4326 },
                DataSource = DataSource.Field,
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            return cabinet.CabinetId;
        });

    private Task<string> NewFeederAsync(string? cabinetId, TopologySource source = TopologySource.Inferred, string? externalRef = null)
        => AsSystemAsync(async db =>
        {
            var feeder = new Feeder
            {
                FeederName = "topology probe feeder",
                CommuneId = fixture.CommuneId,
                CabinetId = cabinetId,
                CabinetSource = cabinetId is null ? null : source,
                ExternalRef = externalRef,
            };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

    private Task<string> NewPoleAsync(string segmentId, string? feederId, TopologySource? source, double lng = Lng)
        => AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = segmentId,
                FeederId = feederId,
                FeederSource = source,
                CommuneId = fixture.CommuneId,
                Geom = new Point(lng, Lat) { SRID = 4326 },
                DataSource = DataSource.Field,
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
}
