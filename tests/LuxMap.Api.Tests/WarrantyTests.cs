using System.Net;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Warranty;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-31 — <c>warranty_status</c> on the map and inventory reads, <c>GET /statistics/warranty</c>, and <c>format=csv</c>.
/// The host runs on a FROZEN clock, so the day edges below cannot move under the test at midnight.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class WarrantyTests(AssetImportFixture factory) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    // Now, frozen: the JWTs this host issues must still validate. The zone/calendar edges themselves are pinned to fixed
    // instants in WarrantyCalendarTests, which needs no host.
    private readonly FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private WebApplicationFactory<Program> host = null!;
    private string home = null!;
    private string foreign = null!;
    private string road = null!;
    private string foreignRoad = null!;

    /// <summary>Today in the communes' zone, by the host's own clock.</summary>
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));

        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[] { new AdministrativeUnit { Name = "Warranty home " + Guid.NewGuid() }, new AdministrativeUnit { Name = "Warranty foreign " + Guid.NewGuid() } };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId; foreign = communes[1].CommuneId;
            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("engineer", UserRole.FieldEngineer), ("superior", UserRole.Superior), ("foreign_superior", UserRole.Superior) })
            {
                var user = new AppUser
                {
                    Username = "wa" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user; db.Add(user);
            }
            await db.SaveChangesAsync();
            foreach (var (key, user) in users)
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = key == "foreign_superior" ? foreign : home });
            var roads = new[] { home, foreign }.Select(c => new RoadSegment
            {
                CommuneId = c, SegmentName = "Warranty road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(107.31, 11.27), new Coordinate(107.32, 11.28)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads);
            await db.SaveChangesAsync();
            road = roads[0].SegmentId; foreignRoad = roads[1].SegmentId;
            return 0;
        });
        foreach (var (key, user) in users)
        {
            var client = host.CreateClient();
            var token = await (await client.PostLoginAsync(user.Username, factory.AccountPassword)).ReadTokensAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            clients[key] = client;
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients.Values) client.Dispose();
        await host.DisposeAsync();
        await Db(async db =>
        {
            foreach (var table in new[] { "fixture", "pole", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, foreign);
            }
            var ids = users.Values.Select(x => x.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {home} OR commune_id = {foreign}");
            return 0;
        });
    }

    // ---- GET /statistics/warranty -----------------------------------------------------------------------

    [Fact]
    public async Task Each_pole_carries_its_status_and_days_left_soonest_expiry_first_and_unknown_last()
    {
        var gone = await Plant(home, road, DataSource.Field, -30);
        var later = await Plant(home, road, DataSource.Field, 400);
        var noLamp = await Plant(home, road, DataSource.Field, lamp: false);
        var noDate = await Plant(home, road, DataSource.Field, null);
        var soon = await Plant(home, road, DataSource.Field, 10);

        var body = await Get("manager", $"warranty?commune_id={home}");

        Assert.Equal(Today.ToString("yyyy-MM-dd"), body.GetProperty("today").GetString());
        Assert.Equal(90, body.GetProperty("expiring_within_days").GetInt32());
        Assert.Equal(5, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([gone, soon, later, noLamp, noDate], items.Select(i => i.GetProperty("pole_id").GetString()));
        Assert.Equal(["expired", "expiring", "active", "unknown", "unknown"], items.Select(i => i.GetProperty("warranty_status").GetString()));
        Assert.Equal([-30, 10, 400], items[..3].Select(i => i.GetProperty("days_remaining").GetInt32()));
        Assert.Equal(JsonValueKind.Null, items[3].GetProperty("days_remaining").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[3].GetProperty("fixture_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[4].GetProperty("warranty_expiry").ValueKind);
        Assert.Equal(new[] { "commune_id", "data_source", "days_remaining", "fixture_id", "fixture_type", "install_date", "lamp_count", "lamp_watt",
                "pole_id", "segment_id", "warranty_expiry", "warranty_status" },
            items[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_status_filter_agrees_with_the_one_definition_on_every_edge()
    {
        var offsets = new int?[] { -1, 0, 90, 91, null };
        foreach (var offset in offsets) await Plant(home, road, DataSource.Field, offset);
        var window = new WarrantyWindow(Today, 90);

        foreach (var status in Enum.GetValues<WarrantyStatus>())
        {
            var wire = Wire(status);
            var items = (await Get("manager", $"warranty?commune_id={home}&status={wire}")).GetProperty("items").EnumerateArray().ToArray();

            var expected = offsets.Count(o => window.Classify(o is { } d ? Today.AddDays(d) : null) == status);
            Assert.Equal(expected, items.Length);
            Assert.All(items, item => Assert.Equal(wire, item.GetProperty("warranty_status").GetString()));
        }

        var two = await Get("manager", $"warranty?commune_id={home}&status=expired,unknown");
        Assert.Equal(2, two.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task With_a_zero_day_window_the_filter_still_agrees_and_a_retired_lamp_is_no_lamp()
    {
        var offsets = new int?[] { -1, 0, 1, null };
        foreach (var offset in offsets) await Plant(home, road, DataSource.Field, offset);
        var retired = await Plant(home, road, DataSource.Field, 300);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            (await db.Set<Fixture>().IgnoreQueryFilters().SingleAsync(f => f.PoleId == retired)).RemovedDate = Today;
            await db.SaveChangesAsync();
            return 0;
        });
        var window = new WarrantyWindow(Today, 0);

        foreach (var status in Enum.GetValues<WarrantyStatus>())
        {
            var items = (await Get("manager", $"warranty?commune_id={home}&within_days=0&status={Wire(status)}"))
                .GetProperty("items").EnumerateArray().ToArray();

            var expected = offsets.Count(o => window.Classify(o is { } d ? Today.AddDays(d) : null) == status)
                + (status == WarrantyStatus.Unknown ? 1 : 0);
            Assert.Equal(expected, items.Length);
            Assert.All(items, item => Assert.Equal(Wire(status), item.GetProperty("warranty_status").GetString()));
        }

        var bare = (await Get("manager", $"warranty?commune_id={home}&within_days=0")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("pole_id").GetString() == retired);
        Assert.Equal(JsonValueKind.Null, bare.GetProperty("fixture_id").ValueKind);
    }

    /// <summary>
    /// Two poles written in ONE transaction share <c>created_at</c>, so the id decides — and by length first: as text
    /// <c>POLE-100000</c> sorts before <c>POLE-99999</c> (section 0).
    /// </summary>
    [Fact]
    public async Task Ties_are_broken_by_id_length_then_id_and_paging_neither_repeats_nor_drops()
    {
        // 9xxxx vs 1xxxxx: compared as text the longer one comes FIRST, so only the length rule puts it second.
        var number = Random.Shared.Next(90_000, 100_000);
        string shorter = $"POLE-{number}", longer = $"POLE-{number + 10_000}";
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            foreach (var id in new[] { longer, shorter })
            {
                db.Add(new Pole { PoleId = id, CommuneId = home, SegmentId = road, DataSource = DataSource.Field, Geom = new Point(107.315, 11.275) { SRID = 4326 } });
                db.Add(Lamp(id, home, Today.AddDays(42)));
            }
            await db.SaveChangesAsync();
            return 0;
        });

        var first = Ids(await Get("manager", $"warranty?commune_id={home}&page_size=1&page=1"));
        var second = Ids(await Get("manager", $"warranty?commune_id={home}&page_size=1&page=2"));

        Assert.Equal([shorter], first);
        Assert.Equal([longer], second);
    }

    [Fact]
    public async Task Within_days_moves_the_window_and_is_bounded()
    {
        await Plant(home, road, DataSource.Field, 20);

        Assert.Equal("expiring", (await Get("manager", $"warranty?commune_id={home}")).GetProperty("items")[0].GetProperty("warranty_status").GetString());
        var narrow = await Get("manager", $"warranty?commune_id={home}&within_days=7");
        Assert.Equal(7, narrow.GetProperty("expiring_within_days").GetInt32());
        Assert.Equal("active", narrow.GetProperty("items")[0].GetProperty("warranty_status").GetString());

        foreach (var bad in new[] { "within_days=-1", "within_days=3651", "status=covered" })
        {
            var response = await clients["manager"].GetAsync($"/api/v1/statistics/warranty?{bad}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.ValidationFailed, await Code(response));
        }
    }

    [Fact]
    public async Task Calibration_rig_poles_and_other_communes_stay_out_unless_asked_for()
    {
        var field = await Plant(home, road, DataSource.Field, 5);
        var rig = await Plant(home, road, DataSource.CalibrationRig, 5);
        var abroad = await Plant(foreign, foreignRoad, DataSource.Field, 5);

        Assert.Equal([field], Ids(await Get("manager", "warranty")).Intersect([field, rig, abroad]));
        Assert.Equal([rig], Ids(await Get("manager", $"warranty?commune_id={home}&data_source=calibration_rig")));
        Assert.Contains(abroad, Ids(await Get("foreign_superior", $"warranty?commune_id={foreign}")));

        var forbidden = await clients["manager"].GetAsync($"/api/v1/statistics/warranty?commune_id={foreign}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(ErrorCodes.CommuneForbidden, await Code(forbidden));
    }

    [Fact]
    public async Task A_field_engineer_cannot_read_the_report()
    {
        var response = await clients["engineer"].GetAsync("/api/v1/statistics/warranty");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.RoleForbidden, await Code(response));
    }

    [Fact]
    public async Task Only_the_lamp_in_service_counts()
    {
        var pole = await Plant(home, road, DataSource.Field, -100);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var old = await db.Set<Fixture>().IgnoreQueryFilters().SingleAsync(f => f.PoleId == pole);
            old.RemovedDate = Today.AddDays(-1);
            await db.SaveChangesAsync();
            db.Add(Lamp(pole, home, Today.AddDays(500)));
            await db.SaveChangesAsync();
            return 0;
        });

        var item = (await Get("manager", $"warranty?commune_id={home}")).GetProperty("items")[0];
        Assert.Equal("active", item.GetProperty("warranty_status").GetString());
        Assert.Equal(500, item.GetProperty("days_remaining").GetInt32());
    }

    // ---- format=csv -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_csv_export_has_every_matching_row_in_list_order_and_ignores_paging()
    {
        var first = await Plant(home, road, DataSource.Field, -2);
        var second = await Plant(home, road, DataSource.Field, 3);

        var response = await clients["manager"].GetAsync($"/api/v1/statistics/warranty?commune_id={home}&format=csv&page_size=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"warranty-{Today:yyyy-MM-dd}.csv", response.Content.Headers.ContentDisposition!.FileName);
        var lines = await Lines(response);
        Assert.Equal("pole_id,segment_id,commune_id,data_source,fixture_id,fixture_type,lamp_watt,lamp_count,install_date,warranty_expiry,warranty_status,days_remaining", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith($"{first},{road},{home},field,", lines[1]);
        Assert.EndsWith($",{Today.AddDays(-2):yyyy-MM-dd},expired,-2", lines[1]);
        Assert.StartsWith($"{second},", lines[2]);
    }

    [Fact]
    public async Task The_dashboard_figures_export_as_csv_too()
    {
        await Plant(home, road, DataSource.Field, 1);

        var status = await Lines(await clients["manager"].GetAsync($"/api/v1/statistics/fixture-status?commune_id={home}&format=csv"));
        Assert.Equal(["data_source,commune_id,segment_id,pole_count,normal,dim,out,unknown,never_surveyed", "field,,,1,0,0,0,1,1"], status);

        var repairs = await Lines(await clients["manager"].GetAsync($"/api/v1/statistics/repair-timeliness?commune_id={home}&format=csv"));
        Assert.Equal(["commune_id,completed,on_time,late,no_due_date,on_time_rate,open_overdue"], repairs);

        var bad = await clients["manager"].GetAsync("/api/v1/statistics/fixture-status?format=xlsx");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    // ---- the read surfaces ------------------------------------------------------------------------------

    [Fact]
    public async Task Map_and_inventory_reads_carry_the_same_status()
    {
        var expired = await Plant(home, road, DataSource.Field, -1);
        var bare = await Plant(home, road, DataSource.Field, lamp: false);

        var map = await Get("manager", "map/poles?bbox=107.30,11.26,107.33,11.29", prefix: "/api/v1/");
        var properties = map.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties"))
            .ToDictionary(p => p.GetProperty("pole_id").GetString()!);
        Assert.Equal("expired", properties[expired].GetProperty("warranty_status").GetString());
        Assert.Equal("unknown", properties[bare].GetProperty("warranty_status").GetString());

        var detail = await Get("manager", $"map/poles/{expired}", prefix: "/api/v1/");
        Assert.Equal("expired", detail.GetProperty("fixture").GetProperty("warranty_status").GetString());

        var inventory = await Get("manager", $"assets/poles/{expired}", prefix: "/api/v1/");
        Assert.Equal("expired", inventory.GetProperty("pole").GetProperty("active_fixture").GetProperty("warranty_status").GetString());
        var list = await Get("manager", $"assets/poles?commune_id={home}", prefix: "/api/v1/");
        Assert.Equal("expired", list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("pole_id").GetString() == expired).GetProperty("active_fixture").GetProperty("warranty_status").GetString());
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private static string Wire(WarrantyStatus status) => JsonSerializer.Serialize(status, LuxMap.Shared.Serialization.LuxMapJsonOptions.Default).Trim('"');

    private static async Task<string?> Code(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString();

    private static string[] Ids(JsonElement body) => body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("pole_id").GetString()!).ToArray();

    private static async Task<string[]> Lines(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        return Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<JsonElement> Get(string who, string path, string prefix = "/api/v1/statistics/")
    {
        var response = await clients[who].GetAsync(prefix + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <param name="expiresIn">Days from today to the warranty's end; <c>null</c> = no expiry on record.</param>
    private Task<string> Plant(string commune, string segment, DataSource source, int? expiresIn = null, bool lamp = true)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var pole = new Pole { CommuneId = commune, SegmentId = segment, DataSource = source, Geom = new Point(107.315, 11.275) { SRID = 4326 } };
            db.Add(pole);
            await db.SaveChangesAsync();
            if (lamp)
            {
                db.Add(Lamp(pole.PoleId, commune, expiresIn is { } days ? Today.AddDays(days) : null));
                await db.SaveChangesAsync();
            }
            return pole.PoleId;
        });

    private Fixture Lamp(string pole, string commune, DateOnly? expiry) => new()
    {
        PoleId = pole, CommuneId = commune, FixtureType = FixtureType.LedRoadLamp, PowerSource = PowerSource.Grid, LampWatt = 60,
        InstallDate = Today.AddYears(-3), WarrantyExpiry = expiry, DataSource = DataSource.Field,
    };
}
