using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary><c>GET /api/v1/faults</c> (BE-40, Contract section 5.4).</summary>
/// <remarks>
/// Every test works inside two throwaway communes, so the shared database's other faults never
/// reach an assertion: the field engineers are scoped to <c>home</c> only, and the manager always
/// narrows with <c>commune_id</c>.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public class FaultListTests(AssetImportFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private const string Route = "/api/v1/faults";

    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private string home = null!;
    private string foreign = null!;
    private string segment = null!;
    private string foreignSegment = null!;

    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var communes = new[]
            {
                new AdministrativeUnit { Name = "Fault list home " + Guid.NewGuid() },
                new AdministrativeUnit { Name = "Fault list foreign " + Guid.NewGuid() },
            };
            db.AddRange(communes);
            await db.SaveChangesAsync();
            home = communes[0].CommuneId;
            foreign = communes[1].CommuneId;

            foreach (var (key, role) in new[] { ("manager", UserRole.Manager), ("a", UserRole.FieldEngineer), ("b", UserRole.FieldEngineer) })
            {
                var user = new AppUser
                {
                    Username = "fl" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = key,
                    PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = role,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, factory.AccountPassword);
                users[key] = user;
                db.Add(user);
            }

            await db.SaveChangesAsync();
            foreach (var user in users.Values)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = home });
            }

            db.Add(new AppUserCommune { UserId = users["manager"].UserId, CommuneId = foreign });

            var roads = new[] { home, foreign }.Select(commune => new RoadSegment
            {
                CommuneId = commune, SegmentName = "Fault list road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 }, LengthM = 100,
            }).ToArray();
            db.AddRange(roads);
            await db.SaveChangesAsync();
            segment = roads[0].SegmentId;
            foreignSegment = roads[1].SegmentId;
            return 0;
        });

        foreach (var (key, user) in users)
        {
            var client = factory.CreateClient();
            var token = await (await client.PostLoginAsync(user.Username, factory.AccountPassword)).ReadTokensAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            clients[key] = client;
        }
    }

    public async Task DisposeAsync()
    {
        await Db(async db =>
        {
            // Every table here holds a RESTRICT reference to the next one (CLAUDE.md, BE-14 trap 3).
            foreach (var table in new[] { "work_order_fault", "work_order", "fault", "pole", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}} OR commune_id = {{1}}";
                await db.Database.ExecuteSqlRawAsync(sql, home, foreign);
            }

            var ids = users.Values.Select(user => user.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM administrative_unit WHERE commune_id = {home} OR commune_id = {foreign}");
            return 0;
        });

        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Each_item_carries_exactly_the_eighteen_contract_keys_in_order()
    {
        var pole = await PlantPoleAsync(home, 108.004, 16.003);
        await PlantAsync(new Plan { Pole = pole, Lat = null, Lng = null, Priority = 12.5 });

        var item = Single(await GetAsync("manager", $"?commune_id={home}"));
        var keys = item.EnumerateObject().Select(property => property.Name).ToArray();
        output.WriteLine(string.Join(", ", keys));

        Assert.Equal(
            ["fault_id", "pole_id", "fixture_id", "segment_id", "location", "fault_type", "fault_status",
             "severity", "source_channel", "data_source", "priority_score", "status_confidence", "cluster_id",
             "detected_at", "updated_at", "work_order_id", "note", "reported_by"],
            keys);
        Assert.Equal(pole, item.GetProperty("pole_id").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("work_order_id").ValueKind);
        Assert.Equal("lamp_out", item.GetProperty("fault_type").GetString());
        Assert.EndsWith("Z", item.GetProperty("detected_at").GetString());

        // No lat/lng on the fault itself → the pole's point, never 0,0.
        Assert.Equal(16.003, item.GetProperty("location").GetProperty("lat").GetDouble(), 9);
        Assert.Equal(108.004, item.GetProperty("location").GetProperty("lng").GetDouble(), 9);
    }

    /// <remarks>
    /// All four rows go in ONE SaveChanges so they share <c>created_at</c> — otherwise the tiebreak
    /// never runs. The two tied IDs sit either side of a width boundary picked above everything the
    /// live table and sequence hold, so they cannot collide (CLAUDE.md, no literal IDs).
    /// </remarks>
    [Fact]
    public async Task Order_is_priority_descending_with_nulls_last_and_ties_by_numeric_id()
    {
        var boundary = await FreeWidthBoundaryAsync();
        var shortId = $"FAULT-{boundary - 1}";
        var longId = $"FAULT-{boundary}";
        output.WriteLine($"Tied pair: {shortId} / {longId}");

        var ids = await PlantAsync(
            new Plan { Id = longId, Priority = 50 },
            new Plan { Id = shortId, Priority = 50 },
            new Plan { Priority = null },
            new Plan { Priority = 90 });
        var (tiedLong, tiedShort, unscored, top) = (ids[0], ids[1], ids[2], ids[3]);

        Assert.Equal([top, tiedShort, tiedLong, unscored], Ids(await GetAsync("manager", $"?commune_id={home}")));
        Assert.Equal(
            [tiedShort, tiedLong, top, unscored],
            Ids(await GetAsync("manager", $"?commune_id={home}&sort=priority_score")));
    }

    [Fact]
    public async Task Work_order_id_is_the_active_link_even_when_the_caller_cannot_open_that_work_order()
    {
        var held = await PlantAsync(new Plan());
        var released = await PlantAsync(new Plan());
        var workOrder = await PlantWorkOrderAsync(users["b"].UserId, (held[0], null), (released[0], DateTime.UtcNow));

        var page = await GetAsync("a", "");
        var byId = page.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("fault_id").GetString()!, item => item.GetProperty("work_order_id"));

        Assert.Equal(workOrder, byId[held[0]].GetString());
        Assert.Equal(JsonValueKind.Null, byId[released[0]].ValueKind);

        // D-3: the ID is emitted although this engineer is refused the work order itself (drift WO-1).
        var open = await clients["a"].GetAsync($"/api/v1/work-orders/{workOrder}");
        Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
    }

    [Fact]
    public async Task Multi_word_enum_values_filter_by_their_wire_name()
    {
        var report = await PlantAsync(new Plan { Channel = SourceChannel.FieldReport, Source = DataSource.Field });
        var offline = await PlantAsync(new Plan { Type = FaultType.NodeOffline, Channel = SourceChannel.Iot });
        var rig = await PlantAsync(new Plan { Source = DataSource.CalibrationRig });

        Assert.Equal(report, Ids(await GetAsync("a", "?source_channel=field_report")));
        Assert.Equal(offline, Ids(await GetAsync("a", "?fault_type=node_offline")));
        Assert.Equal(rig, Ids(await GetAsync("a", "?data_source=calibration_rig")));

        // The testbed is hidden unless asked for by name (D-6).
        Assert.DoesNotContain(rig[0], Ids(await GetAsync("a", "")));
        Assert.Contains(rig[0], Ids(await GetAsync("a", "?data_source=calibration_rig,simulated")));

        var refused = await clients["a"].GetAsync(Route + "?source_channel=manual");
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, body);
        Assert.Contains("field_report", body);
    }

    [Fact]
    public async Task Status_and_severity_take_comma_separated_lists()
    {
        var confirmed = await PlantAsync(new Plan { Status = FaultStatus.Confirmed, Severity = Severity.High });
        var rejected = await PlantAsync(new Plan { Status = FaultStatus.Rejected, Severity = Severity.Low });
        await PlantAsync(new Plan { Status = FaultStatus.Detected, Severity = Severity.Low });

        Assert.Equal(
            new[] { confirmed[0], rejected[0] }.Order(),
            Ids(await GetAsync("a", "?status=confirmed,rejected")).Order());
        Assert.Equal(confirmed, Ids(await GetAsync("a", "?severity=high,critical")));
    }

    [Fact]
    public async Task A_foreign_commune_is_403_but_an_unknown_or_foreign_pole_is_the_same_empty_page()
    {
        var foreignPole = await PlantPoleAsync(foreign, 108.002, 16.002, foreignSegment);
        await PlantAsync(new Plan { Commune = foreign, Segment = foreignSegment, Pole = foreignPole });

        var forbidden = await clients["a"].GetAsync(Route + $"?commune_id={foreign}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains(ErrorCodes.CommuneForbidden, await forbidden.Content.ReadAsStringAsync());

        var outOfScope = await GetAsync("a", $"?pole_id={foreignPole}");
        var unknown = await GetAsync("a", "?pole_id=POLE-DOES-NOT-EXIST");
        Assert.Equal(0, outOfScope.GetProperty("total").GetInt32());
        Assert.Equal(outOfScope.GetRawText(), unknown.GetRawText());

        // The manager, who holds both communes, does see it.
        Assert.Single(Ids(await GetAsync("manager", $"?pole_id={foreignPole}")));
    }

    [Fact]
    public async Task Bbox_filters_on_the_located_point_including_the_pole_fallback()
    {
        var pole = await PlantPoleAsync(home, 108.005, 16.005);
        var inside = await PlantAsync(new Plan { Lat = 16.001, Lng = 108.001 });
        var viaPole = await PlantAsync(new Plan { Pole = pole, Lat = null, Lng = null });
        await PlantAsync(new Plan { Lat = 17, Lng = 109 });

        Assert.Equal(
            new[] { inside[0], viaPole[0] }.Order(),
            Ids(await GetAsync("a", "?bbox=108,16,108.01,16.01")).Order());

        var bad = await clients["a"].GetAsync(Route + "?bbox=108,16,NaN,16.01");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task An_unknown_sort_is_refused_with_the_allowed_list()
    {
        var response = await clients["a"].GetAsync(Route + "?sort=fault_id");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("-priority_score", body);
        Assert.Contains("updated_at", body);
    }

    [Fact]
    public async Task Total_counts_every_match_while_items_hold_one_page()
    {
        await PlantAsync(new Plan(), new Plan(), new Plan());

        var page = await GetAsync("a", "?page=2&page_size=2");

        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("page_size").GetInt32());
        Assert.Single(Ids(page));
    }

    private sealed record Plan
    {
        public string? Id { get; init; }
        public string? Commune { get; init; }
        public string? Segment { get; init; }
        public string? Pole { get; init; }
        public double? Lat { get; init; } = 16.001;
        public double? Lng { get; init; } = 108.001;
        public double? Priority { get; init; }
        public FaultStatus Status { get; init; } = FaultStatus.Detected;
        public Severity Severity { get; init; } = Severity.Medium;
        public FaultType Type { get; init; } = FaultType.LampOut;
        public SourceChannel Channel { get; init; } = SourceChannel.Cv;
        public DataSource Source { get; init; } = DataSource.Simulated;
    }

    /// <summary>Writes every plan in ONE SaveChanges — they share <c>created_at</c>.</summary>
    private Task<string[]> PlantAsync(params Plan[] plans)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var faults = plans.Select(plan =>
            {
                var fault = new Fault
                {
                    CommuneId = plan.Commune ?? home, SegmentId = plan.Segment ?? segment, PoleId = plan.Pole,
                    Lat = plan.Lat, Lng = plan.Lng, PriorityScore = plan.Priority,
                    FaultStatus = plan.Status, Severity = plan.Severity, FaultType = plan.Type,
                    SourceChannel = plan.Channel, DataSource = plan.Source, DetectedAt = DateTime.UtcNow,
                };
                if (plan.Id is not null)
                {
                    fault.FaultId = plan.Id;
                }

                return fault;
            }).ToArray();
            db.AddRange(faults);
            await db.SaveChangesAsync();
            return faults.Select(fault => fault.FaultId).ToArray();
        });

    private Task<string> PlantPoleAsync(string commune, double lng, double lat, string? road = null)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var pole = new Pole
            {
                CommuneId = commune, SegmentId = road ?? segment, DataSource = DataSource.Simulated,
                Geom = new Point(lng, lat) { SRID = 4326 },
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private Task<string> PlantWorkOrderAsync(string assignee, params (string Fault, DateTime? Released)[] links)
        => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var now = DateTime.UtcNow;
            var workOrder = new WorkOrder
            {
                CommuneId = home, Title = "Fault list work order", CreatedBy = users["manager"].UserId,
                TaskKind = TaskKind.Inspection, WoStatus = WorkOrderStatus.Assigned, SegmentId = segment,
                AssignedTo = assignee, AssignedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Add(workOrder);
            await db.SaveChangesAsync();
            foreach (var (fault, released) in links)
            {
                db.Add(new WorkOrderFault
                {
                    WorkOrderId = workOrder.WorkOrderId, FaultId = fault, CommuneId = home,
                    LinkedAt = now.AddMinutes(-1), ReleasedAt = released,
                });
            }

            await db.SaveChangesAsync();
            return workOrder.WorkOrderId;
        });

    /// <summary>
    /// A power of ten above every fault ID the live table and sequence hold, at least six digits, so
    /// <c>boundary - 1</c> and <c>boundary</c> straddle a width change and are both free.
    /// </summary>
    private Task<long> FreeWidthBoundaryAsync()
        => Db(async db =>
        {
            var highest = await db.Database.SqlQueryRaw<long>(
                    """
                    SELECT GREATEST(
                        (SELECT COALESCE(MAX(substring(fault_id FROM 7)::bigint), 0) FROM fault
                          WHERE fault_id ~ '^FAULT-[0-9]+$'),
                        (SELECT last_value FROM fault_id_seq)) AS "Value"
                    """)
                .SingleAsync();
            var digits = Math.Max(highest.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 1, 6);
            return (long)Math.Pow(10, digits - 1);
        });

    private async Task<JsonElement> GetAsync(string who, string query)
    {
        var response = await clients[who].GetAsync(Route + query);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{who} GET {query}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string[] Ids(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fault_id").GetString()!).ToArray();

    private static JsonElement Single(JsonElement page) => Assert.Single(page.GetProperty("items").EnumerateArray());
}
