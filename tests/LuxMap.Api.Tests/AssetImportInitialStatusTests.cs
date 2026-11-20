using System.Net;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// IMPORT-INITIAL-STATUS — a demo pole created by an import can arrive already lit: <c>initial_status</c> writes the first
/// <c>pole_current_status</c> row, only for <c>simulated</c> data and only for poles the file creates.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetImportInitialStatusTests(AssetImportFixture fixture) : AssetWorkbookTestContext(fixture)
{
    private const string Header = "external_ref,commune_id,geom_wkt,data_source,initial_status";
    private const string Bbox = "bbox=106.4899,10.9699,106.4905,10.9705";

    [Fact]
    public async Task A_simulated_pole_imported_with_normal_is_green_on_the_map_without_any_survey()
    {
        var result = await ImportCsv($"{Header}\nG-001,{Commune},POINT(106.4900 10.9700),simulated,normal");
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());

        var status = await StatusOf("G-001");
        Assert.NotNull(status);
        Assert.Equal("normal", Name(status!.FixtureStatus));
        Assert.Null(status.StatusConfidence);
        Assert.Null(status.LastSweepId);
        Assert.Null(status.LastRunId);
        Assert.NotNull(status.LastSeenAt);
        Assert.Equal(status.LastSeenAt, status.LastEvaluatedAt);
        Assert.Equal(Commune, status.CommuneId);

        Assert.Equal("normal", await MapStatus("G-001"));
    }

    [Theory]
    [InlineData("dim")]
    [InlineData("out")]
    public async Task The_other_known_statuses_can_be_seeded_too(string value)
    {
        await ImportCsv($"{Header}\nG-002,{Commune},POINT(106.4901 10.9700),simulated,{value}");
        Assert.Equal(value, await MapStatus("G-002"));
    }

    [Fact]
    public async Task A_blank_cell_or_a_missing_column_leaves_the_pole_unknown()
    {
        await ImportCsv($"{Header}\nG-003,{Commune},POINT(106.4902 10.9700),simulated,");
        await ImportCsv($"external_ref,commune_id,geom_wkt,data_source\nG-004,{Commune},POINT(106.4903 10.9700),simulated");

        Assert.Null(await StatusOf("G-003"));
        Assert.Null(await StatusOf("G-004"));
        Assert.Equal("unknown", await MapStatus("G-003"));
    }

    [Fact]
    public async Task A_pole_without_a_position_still_gets_its_status()
    {
        var result = await ImportCsv($"{Header}\nG-005,{Commune},,simulated,normal");
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal("normal", Name((await StatusOf("G-005"))!.FixtureStatus));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("public_imagery")]
    [InlineData("calibration_rig")]
    public async Task Any_source_but_simulated_is_refused_and_nothing_is_written(string source)
    {
        var result = await ImportCsv($"{Header}\nG-006,{Commune},POINT(106.4904 10.9700),{source},normal");

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var error = result.GetProperty("rows")[0];
        Assert.Equal("initial_status", error.GetProperty("column").GetString());
        Assert.Contains("simulated", error.GetProperty("message").GetString()!);
        Assert.False(await PoleExists("G-006"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("green")]
    [InlineData("Normal")]
    public async Task Only_normal_dim_or_out_are_accepted(string value)
    {
        var result = await ImportCsv($"{Header}\nG-007,{Commune},POINT(106.4904 10.9700),simulated,{value}");

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var error = result.GetProperty("rows")[0];
        Assert.Equal("initial_status", error.GetProperty("column").GetString());
        Assert.Contains("normal, dim, out", error.GetProperty("message").GetString()!);
        Assert.False(await PoleExists("G-007"));
    }

    [Fact]
    public async Task A_pole_that_already_exists_keeps_its_state_and_the_row_is_warned_about()
    {
        await ImportCsv($"{Header}\nG-008,{Commune},POINT(106.4905 10.9700),simulated,");
        var again = await ImportCsv($"{Header}\nG-008,{Commune},POINT(106.4905 10.9700),simulated,out");

        Assert.Equal(0, again.GetProperty("inserted").GetInt32());
        Assert.Equal(1, again.GetProperty("unchanged").GetInt32());
        Assert.Equal(1, again.GetProperty("total_warnings").GetInt32());
        Assert.Equal("initial_status", again.GetProperty("warnings")[0].GetProperty("column").GetString());
        Assert.Null(await StatusOf("G-008"));
    }

    [Fact]
    public async Task A_status_already_stored_is_never_overwritten_by_a_reimport()
    {
        await ImportCsv($"{Header}\nG-009,{Commune},POINT(106.4900 10.9701),simulated,normal");
        await ImportCsv($"{Header}\nG-009,{Commune},POINT(106.4900 10.9701),simulated,out");

        Assert.Equal("normal", Name((await StatusOf("G-009"))!.FixtureStatus));
    }

    [Fact]
    public async Task The_workbook_pole_sheet_takes_the_column_under_the_same_rules()
    {
        var row = Row(("external_ref", "WB-1"), ("geom_wkt", "POINT(106.4901 10.9701)"), ("initial_status", "normal"));
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, row);

        using var accepted = await Upload(AssetWorkbookFile.Create(sheet), "simulated", Commune);
        var body = await accepted.Content.ReadAsStringAsync();
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, body);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("committed").GetBoolean(), body);
        Assert.Equal("normal", Name((await StatusOf("WB-1"))!.FixtureStatus));

        var other = AssetWorkbookFile.Asset(ImportKind.Poles,
            Row(("external_ref", "WB-2"), ("geom_wkt", "POINT(106.4902 10.9701)"), ("initial_status", "normal")));
        using var refused = await Upload(AssetWorkbookFile.Create(other), "field", Commune);
        var refusedBody = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.False(refusedBody.GetProperty("committed").GetBoolean());
        Assert.Contains(refusedBody.GetProperty("rows").EnumerateArray(), error =>
            error.GetProperty("column").GetString() == "initial_status" && error.GetProperty("sheet").GetString() == "Cột điện");
        Assert.False(await PoleExists("WB-2"));
    }

    [Fact]
    public async Task A_workbook_without_the_column_still_imports()
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "WB-3"), ("geom_wkt", "POINT(106.4903 10.9701)")))
            with { Columns = ["external_ref", "geom_wkt"] };

        using var response = await Upload(AssetWorkbookFile.Create(sheet), "simulated", Commune);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("committed").GetBoolean(), body);
        Assert.Null(await StatusOf("WB-3"));
    }

    private async Task<JsonElement> ImportCsv(string body)
    {
        using var client = Client();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(body)), "file", "poles.csv");
        using var response = await client.PostAsync("/api/v1/assets/import/poles", content);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Import answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private Task<PoleCurrentStatus?> StatusOf(string externalRef) => Fixture.QueryAsync(db =>
        (from pole in db.Set<Pole>().IgnoreQueryFilters()
         join status in db.Set<PoleCurrentStatus>().IgnoreQueryFilters() on pole.PoleId equals status.PoleId
         where pole.CommuneId == Commune && pole.ExternalRef == externalRef
         select status).SingleOrDefaultAsync());

    private Task<bool> PoleExists(string externalRef) => Fixture.QueryAsync(db =>
        db.Set<Pole>().IgnoreQueryFilters().AnyAsync(pole => pole.CommuneId == Commune && pole.ExternalRef == externalRef));

    /// <summary>What the map layer says for the pole — the colour the web draws.</summary>
    private async Task<string> MapStatus(string externalRef)
    {
        var poleId = await Fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(pole => pole.CommuneId == Commune && pole.ExternalRef == externalRef).Select(pole => pole.PoleId).SingleAsync());
        using var client = Client();
        var map = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/map/poles?{Bbox}&data_source=simulated")).RootElement;
        return map.GetProperty("features").EnumerateArray().Select(feature => feature.GetProperty("properties"))
            .Single(properties => properties.GetProperty("pole_id").GetString() == poleId)
            .GetProperty("fixture_status").GetString()!;
    }

    private static string Name(LuxMap.Shared.Contracts.Enums.FixtureStatus status) => LuxMap.Shared.Http.WireEnum.Name(status);
}
