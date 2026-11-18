using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

public abstract class AssetWorkbookTestContext(AssetImportFixture fixture) : IAsyncLifetime
{
    protected AssetImportFixture Fixture => fixture;
    protected string Commune = null!;
    protected string OtherCommune = null!;
    public async Task InitializeAsync()
    {
        (Commune, OtherCommune) = await fixture.QueryAsync(async db =>
        {
            var a = new AdministrativeUnit { Name = $"wb-multi-a-{Guid.NewGuid():N}" };
            var b = new AdministrativeUnit { Name = $"wb-multi-b-{Guid.NewGuid():N}" };
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.CommuneId, b.CommuneId);
        });
    }
    public async Task DisposeAsync()
    {
        await fixture.QueryAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            foreach (var table in new[] { "audit_event", "fixture", "pole", "feeder", "electrical_cabinet", "road_segment", "administrative_unit" })
            {
                // Table names are a fixed internal list; commune IDs remain parameters.
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, Commune, OtherCommune);
            }
            await tx.CommitAsync();
            return 0;
        });
    }
    protected HttpClient Client(UserRole role = UserRole.Manager, string[]? communes = null)
    {
        var user = new AppUser { UserId = fixture.ManagerUserId, Username = fixture.ManagerUsername,
            Email = $"{fixture.ManagerUsername}@luxmap.local", FullName = "Multisheet tests", Role = role,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm, HasSystemWideScope = role == UserRole.SystemAdmin };
        var token = fixture.Services.GetRequiredService<AccessTokenIssuer>().Issue(user,
            role == UserRole.SystemAdmin ? ["*"] : communes ?? [Commune]).Token;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
    protected async Task<HttpResponseMessage> Upload(byte[] bytes, string? source = "public_imagery", string? commune = null,
        string[]? scope = null, string name = "inventory.xlsx")
    {
        using var client = Client(communes: scope);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", name);
        if (source is not null) content.Add(new StringContent(source), "data_source");
        if (commune is not null) content.Add(new StringContent(commune), "commune_id");
        return await client.PostAsync("/api/v1/assets/import/workbook", content);
    }
    private protected async Task<JsonElement> Import(params AssetWorkbookFile.Sheet[] sheets) => await ImportBytes(AssetWorkbookFile.Create(sheets));
    protected async Task<JsonElement> ImportBytes(byte[] bytes)
    {
        using var response = await Upload(bytes, commune: Commune);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }
    protected static Dictionary<string, object?> Row(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value);
    private protected static AssetWorkbookFile.Sheet[] Full() => [
        AssetWorkbookFile.Asset(ImportKind.Segments, Row(("external_ref", "TUYEN-A"), ("segment_name", "Route"), ("road_class", "inter_village"),
            ("length_m", 100), ("geom_wkt", "LINESTRING(106.49 10.97, 106.50 10.98)"))),
        AssetWorkbookFile.Asset(ImportKind.Cabinets, Row(("external_ref", "TU-A"), ("cabinet_name", "Cabinet"), ("geom_wkt", "POINT(106.49 10.97)"))),
        AssetWorkbookFile.Asset(ImportKind.Feeders, Row(("external_ref", "MACH-A"), ("feeder_name", "Feeder"), ("cabinet_external_ref", "TU-A"),
            ("cabinet_source", "verified"), ("geom_wkt", "LINESTRING(106.49 10.97, 106.50 10.98)"))),
        AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "007"), ("segment_external_ref", "TUYEN-A"),
            ("feeder_external_ref", "MACH-A"), ("feeder_source", "verified"), ("geom_wkt", "POINT(106.4900123456789 10.9700123456789)"), ("near_sensitive_poi", true))),
        AssetWorkbookFile.Asset(ImportKind.Fixtures, Fitting("2020-01-01", "2021-01-01"), Fitting("2022-03-24")),
    ];
    protected static Dictionary<string, object?> Fitting(string install, string? removed = null)
        => Row(("pole_external_ref", "007"), ("fixture_type", "led_road_lamp"), ("power_source", "grid"), ("lamp_watt", 100),
            ("lamp_count", 1), ("install_date", install), ("removed_date", removed), ("warranty_expiry", "2028-03-24"), ("note", "Lamp"));
    protected async Task<string[]> Snapshot() => await fixture.QueryAsync(async db =>
    {
        var result = new List<string>();
        var segments = await db.Set<RoadSegment>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync();
        result.AddRange(segments.Select(x => $"S/{x.SegmentId}/{x.SegmentName}/{x.Geom?.AsText()}/{x.UpdatedAt:O}/{x.UpdatedBy}/{x.DataSource}"));
        var cabinets = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync();
        result.AddRange(cabinets.Select(x => $"C/{x.CabinetId}/{x.CabinetName}/{x.Geom.AsText()}/{x.UpdatedAt:O}/{x.UpdatedBy}/{x.DataSource}"));
        var feeders = await db.Set<Feeder>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync();
        result.AddRange(feeders.Select(x => $"F/{x.FeederId}/{x.FeederName}/{x.Geom?.AsText()}/{x.CabinetId}/{x.CabinetSource}/{x.UpdatedAt:O}/{x.UpdatedBy}"));
        var poles = await db.Set<Pole>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync();
        result.AddRange(poles.Select(x => $"P/{x.PoleId}/{x.Geom?.AsText()}/{x.Note}/{x.SegmentId}/{x.FeederId}/{x.FeederSource}/{x.UpdatedAt:O}/{x.UpdatedBy}/{x.DataSource}"));
        var fixtures = await db.Set<Fixture>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync();
        result.AddRange(fixtures.Select(x => $"L/{x.FixtureId}/{x.PoleId}/{x.InstallDate}/{x.RemovedDate}/{x.LampWatt}/{x.UpdatedAt:O}/{x.UpdatedBy}/{x.DataSource}"));
        return result.Order(StringComparer.Ordinal).ToArray();
    });
    protected static void Committed(JsonElement result) => Assert.True(result.GetProperty("committed").GetBoolean(), result.ToString());
    protected static void Rejected(JsonElement result)
    {
        Assert.False(result.GetProperty("committed").GetBoolean(), result.ToString());
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
            foreach (var count in new[] { "inserted", "updated", "unchanged" }) Assert.Equal(0, result.GetProperty(kind).GetProperty(count).GetInt32());
        Assert.Equal(0, result.GetProperty("total_warnings").GetInt32());
    }
    protected static void Error(JsonElement result, string? sheet, int row, string? column, string message)
        => Assert.Contains(result.GetProperty("rows").EnumerateArray(), e => e.GetProperty("sheet").GetString() == sheet
            && e.GetProperty("row").GetInt32() == row && e.GetProperty("column").GetString() == column
            && e.GetProperty("message").GetString()!.Contains(message, StringComparison.OrdinalIgnoreCase));
}
