using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12a — bulk import: validation before writing, upsert, insert-only fixtures, and the batch
/// transaction.
/// </summary>
[Collection(nameof(AssetImportCollection))]
public sealed class AssetImportTests(AssetImportFixture fixture)
{
    private static readonly string SegmentHeader =
        "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source";

    private static readonly string PoleHeader =
        "external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source";

    private static readonly string FixtureHeader =
        "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source";

    [Fact]
    public async Task A_reference_that_matches_nothing_is_caught_in_validation_and_nothing_is_written()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();

        var result = await ImportAsync(client, "poles", "poles.csv",
            PoleHeader
            + $"\n{tag}-P1,{tag}-DOES-NOT-EXIST,,{fixture.CommuneId},POINT(106.49 10.97),false,public_imagery");

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());

        var error = result.GetProperty("rows")[0];
        Assert.Equal("segment_external_ref", error.GetProperty("column").GetString());
        Assert.Contains("matches nothing", error.GetProperty("message").GetString()!);

        // The point of validating the whole file first: a bad foreign key never reaches the database,
        // so it can never be the thing that aborts the batch.
        Assert.Equal(0, await CountPolesAsync(tag));
    }

    [Fact]
    public async Task Loading_the_same_file_twice_updates_instead_of_creating_a_second_copy()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();

        var file = SegmentHeader
            + $"\n{tag}-S1,Tuyen mot,inter_commune,1600,"
            + "\"LINESTRING(106.4900 10.9700, 106.4950 10.9705)\","
            + $"{fixture.CommuneId},public_imagery";

        var first = await ImportAsync(client, "segments", "segments.csv", file);
        Assert.Equal(1, first.GetProperty("inserted").GetInt32());
        Assert.Equal(0, first.GetProperty("updated").GetInt32());

        var renamed = file.Replace("Tuyen mot", "Tuyen mot - da doi ten", StringComparison.Ordinal);
        var second = await ImportAsync(client, "segments", "segments.csv", renamed);

        Assert.Equal(0, second.GetProperty("inserted").GetInt32());
        Assert.Equal(1, second.GetProperty("updated").GetInt32());

        var rows = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.ExternalRef == $"{tag}-S1")
            .ToListAsync());

        Assert.Single(rows);
        Assert.Equal("Tuyen mot - da doi ten", rows[0].SegmentName);
    }

    [Fact]
    public async Task A_second_fixtures_file_is_refused_per_row_rather_than_doubling_the_equipment_history()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();
        await SeedPoleAsync(client, tag);

        var file = FixtureHeader + $"\n{tag}-P1,led_road_lamp,grid,100,2022-03-24,,2024-03-24,public_imagery";

        var first = await ImportAsync(client, "fixtures", "fixtures.csv", file);
        Assert.Equal(1, first.GetProperty("inserted").GetInt32());

        var second = await ImportAsync(client, "fixtures", "fixtures.csv", file);
        Assert.Equal(0, second.GetProperty("inserted").GetInt32());
        Assert.Equal(1, second.GetProperty("failed").GetInt32());
        Assert.Contains("already carries a fixture", second.GetProperty("rows")[0].GetProperty("message").GetString()!);

        var count = await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(item => item.Pole.ExternalRef == $"{tag}-P1"));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Two_rows_in_ONE_file_claiming_the_same_pole_are_caught_before_the_write()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();
        await SeedPoleAsync(client, tag);

        var result = await ImportAsync(client, "fixtures", "fixtures.csv",
            FixtureHeader
            + $"\n{tag}-P1,led_road_lamp,grid,100,2022-03-24,,,public_imagery"
            + $"\n{tag}-P1,led_road_lamp,grid,150,2023-01-01,,,public_imagery");

        // The in-batch check matters as much as the database one: both rows are new, so nothing in
        // the schema would have stopped the second.
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task A_row_naming_a_commune_outside_the_scope_fails_that_row_and_leaves_the_rest_written()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();

        var result = await ImportAsync(client, "segments", "segments.csv",
            SegmentHeader
            + $"\n{tag}-OK,Trong pham vi,inter_commune,100,\"LINESTRING(106.49 10.97, 106.50 10.98)\","
            + $"{fixture.CommuneId},public_imagery"
            + $"\n{tag}-NO,Ngoai pham vi,inter_commune,100,\"LINESTRING(106.49 10.97, 106.50 10.98)\","
            + $"{fixture.ForeignCommuneId},public_imagery");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());

        var error = result.GetProperty("rows")[0];
        Assert.Equal("commune_id", error.GetProperty("column").GetString());
        Assert.Contains("outside your permitted commune scope", error.GetProperty("message").GetString()!);

        var written = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.ExternalRef!.StartsWith(tag))
            .Select(segment => segment.ExternalRef)
            .ToListAsync());

        Assert.Equal([$"{tag}-OK"], written);
    }

    [Fact]
    public async Task More_than_a_hundred_errors_are_truncated_but_the_total_is_still_reported_in_full()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();

        // 150 rows, each missing road_class AND data_source — so 300 errors from 150 rows, which also
        // proves total_errors counts ERRORS rather than rows.
        var file = new StringBuilder(SegmentHeader);
        for (var i = 0; i < 150; i++)
        {
            file.Append($"\n{tag}-{i},Ten,,100,\"LINESTRING(106.49 10.97, 106.50 10.98)\",{fixture.CommuneId},");
        }

        var result = await ImportAsync(client, "segments", "segments.csv", file.ToString());

        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.Equal(100, result.GetProperty("rows").GetArrayLength());
        Assert.Equal(300, result.GetProperty("total_errors").GetInt32());
        Assert.Equal(150, result.GetProperty("failed").GetInt32());
        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task A_file_whose_header_is_missing_a_column_reports_it_once_against_line_one()
    {
        var client = await fixture.AdminClientAsync();

        var result = await ImportAsync(client, "segments", "segments.csv",
            "external_ref,segment_name\nA,Ten");

        var error = result.GetProperty("rows")[0];
        Assert.Equal(1, error.GetProperty("row").GetInt32());
        Assert.Contains("road_class", error.GetProperty("column").GetString()!);
        Assert.Contains("Column missing", error.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task An_unsupported_file_extension_is_refused_with_415_before_anything_is_parsed()
    {
        var client = await fixture.AdminClientAsync();

        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes("irrelevant"));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(bytes, "file", "inventory.xlsx");

        var response = await client.PostAsync("/api/v1/assets/import/segments", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_import_kind_is_a_400_naming_the_four_that_exist()
    {
        var client = await fixture.AdminClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("a,b\n1,2")), "file", "x.csv");

        var response = await client.PostAsync("/api/v1/assets/import/lampposts", content);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("segments", body.GetProperty("error").GetProperty("details").GetProperty("kind").GetString()!);
    }

    [Fact]
    public async Task A_semicolon_file_with_a_utf8_BOM_and_CRLF_still_imports()
    {
        var client = await fixture.AdminClientAsync();
        var tag = Tag();

        // Everything Excel does wrong at once, which is the realistic case rather than the tidy one.
        var file = "﻿" + SegmentHeader.Replace(',', ';')
            + $"\r\n{tag}-EX;Tuyen Excel;inter_commune;1600;"
            + "\"LINESTRING(106.4900 10.9700, 106.4950 10.9705)\";"
            + $"{fixture.CommuneId};public_imagery\r\n";

        var result = await ImportAsync(client, "segments", "segments.csv", file);

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());
    }

    /// <summary>
    /// A row in a commune the caller cannot see is invisible to the upsert, so the same inventory code
    /// is free to reuse.
    /// </summary>
    /// <remarks>
    /// ⚠️ This pins the BE-08 QUERY FILTER, not the upsert key — it would pass just as well if the key
    /// were <c>external_ref</c> alone, because the foreign row never reaches the dictionary to collide
    /// with. The key itself is pinned by
    /// <see cref="The_upsert_key_is_the_COMPOSITE_when_both_communes_are_inside_the_scope"/>, which is
    /// the case where the filter admits both rows.
    /// </remarks>
    [Fact]
    public async Task A_row_in_an_unreachable_commune_does_not_block_reusing_its_inventory_code()
    {
        var tag = Tag();
        var shared = $"{tag}-TUYEN-A";

        // The foreign commune's row is planted as the system, because the test administrator is
        // scoped to one commune and must not be able to reach the other.
        await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.Set<RoadSegment>().Add(new RoadSegment
                {
                    ExternalRef = shared,
                    SegmentName = "Tuyen cua xa khac",
                    RoadClass = LuxMap.Shared.Contracts.Enums.RoadClass.InterVillage,
                    LengthM = 500,
                    Geom = new NetTopologySuite.Geometries.LineString(
                        [new(106.40, 10.90), new(106.41, 10.91)]) { SRID = 4326 },
                    CommuneId = fixture.ForeignCommuneId,
                    DataSource = LuxMap.Shared.Contracts.Enums.DataSource.PublicImagery,
                });

                return await db.SaveChangesAsync();
            }
        });

        var client = await fixture.AdminClientAsync();
        var result = await ImportAsync(client, "segments", "segments.csv",
            SegmentHeader
            + $"\n{shared},Tuyen cua xa minh,inter_commune,1600,"
            + $"\"LINESTRING(106.4900 10.9700, 106.4950 10.9705)\",{fixture.CommuneId},public_imagery");

        // INSERT, not UPDATE: the code collides but the commune does not, so this is a different row.
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(0, result.GetProperty("updated").GetInt32());

        var rows = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.ExternalRef == shared)
            .OrderBy(segment => segment.CommuneId)
            .Select(segment => new { segment.CommuneId, segment.SegmentName })
            .ToListAsync());

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.CommuneId == fixture.CommuneId && row.SegmentName == "Tuyen cua xa minh");
        Assert.Contains(rows, row => row.CommuneId == fixture.ForeignCommuneId && row.SegmentName == "Tuyen cua xa khac");
    }

    /// <summary>
    /// The upsert key is <c>(commune_id, external_ref)</c> — the unique index — and NOT the code alone.
    /// </summary>
    /// <remarks>
    /// The discriminating case, and the only one that is: a caller whose <c>commune_ids</c> covers BOTH
    /// communes. The query filter now admits both rows carrying the same code, so they meet in the same
    /// lookup and the key is the only thing keeping them apart.
    /// <para>
    /// With a key of <c>external_ref</c> alone this test cannot pass: building the lookup would either
    /// throw on the duplicate key or silently keep one row, and the import would then UPDATE the wrong
    /// commune's asset — overwriting a road in a commune the operator never named.
    /// </para>
    /// <para>
    /// It matters because the two halves live apart and agree only by construction: the SQL narrows on
    /// <c>external_ref</c>, and the composite key is applied when the result is indexed in memory.
    /// Nothing in the schema forbids two communes numbering a road the same way.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_upsert_key_is_the_COMPOSITE_when_both_communes_are_inside_the_scope()
    {
        var tag = Tag();
        var shared = $"{tag}-TUYEN-A";
        var client = await fixture.BothCommunesClientAsync();

        // Both rows are created through the API by the two-commune administrator, so both are plainly
        // inside the scope — no backdoor, nothing hidden by the filter.
        foreach (var (commune, name) in new[]
                 { (fixture.CommuneId, "Tuyen xa mot"), (fixture.ForeignCommuneId, "Tuyen xa hai") })
        {
            var created = await ImportAsync(client, "segments", "segments.csv",
                SegmentHeader
                + $"\n{shared},{name},inter_commune,100,"
                + $"\"LINESTRING(106.49 10.97, 106.50 10.98)\",{commune},public_imagery");

            Assert.Equal(1, created.GetProperty("inserted").GetInt32());
        }

        // Sanity: this caller really can see both, which is what makes the case discriminating.
        Assert.Equal(2, await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .CountAsync(segment => segment.ExternalRef == shared)));

        // Now update ONLY the second commune's row.
        var updated = await ImportAsync(client, "segments", "segments.csv",
            SegmentHeader
            + $"\n{shared},Tuyen xa hai - da doi ten,inter_commune,100,"
            + $"\"LINESTRING(106.49 10.97, 106.50 10.98)\",{fixture.ForeignCommuneId},public_imagery");

        Assert.Equal(1, updated.GetProperty("updated").GetInt32());
        Assert.Equal(0, updated.GetProperty("inserted").GetInt32());

        var rows = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.ExternalRef == shared)
            .Select(segment => new { segment.CommuneId, segment.SegmentName })
            .ToListAsync());

        Assert.Equal(2, rows.Count);

        // The other commune's road is untouched. Matching on the code alone would have renamed it.
        Assert.Contains(rows, row => row.CommuneId == fixture.CommuneId && row.SegmentName == "Tuyen xa mot");
        Assert.Contains(rows, row =>
            row.CommuneId == fixture.ForeignCommuneId && row.SegmentName == "Tuyen xa hai - da doi ten");
    }

    private static string Tag() => $"T{Guid.NewGuid():N}"[..9].ToUpperInvariant();

    private async Task SeedPoleAsync(HttpClient client, string tag)
    {
        var segments = await ImportAsync(client, "segments", "segments.csv",
            SegmentHeader
            + $"\n{tag}-S1,Tuyen,inter_commune,1600,\"LINESTRING(106.4900 10.9700, 106.4950 10.9705)\","
            + $"{fixture.CommuneId},public_imagery");
        Assert.Equal(1, segments.GetProperty("inserted").GetInt32());

        var poles = await ImportAsync(client, "poles", "poles.csv",
            PoleHeader
            + $"\n{tag}-P1,{tag}-S1,,{fixture.CommuneId},POINT(106.4900 10.9700),false,public_imagery");
        Assert.Equal(1, poles.GetProperty("inserted").GetInt32());
    }

    private Task<int> CountPolesAsync(string tag)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .CountAsync(pole => pole.ExternalRef!.StartsWith(tag)));

    internal static async Task<JsonElement> ImportAsync(
        HttpClient client, string kind, string fileName, string body)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(body)), "file", fileName);

        var response = await client.PostAsync($"/api/v1/assets/import/{kind}", content);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Import answered {(int)response.StatusCode}: {text}");

        return JsonDocument.Parse(text).RootElement;
    }
}
