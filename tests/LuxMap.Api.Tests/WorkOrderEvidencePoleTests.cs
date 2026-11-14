using System.Data.Common;
using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    [Theory]
    [InlineData(TaskKind.Inspection)]
    [InlineData(TaskKind.Repair)]
    [InlineData(TaskKind.Survey)]
    [InlineData(TaskKind.PoleGpsUpdate)]
    public async Task Evidence_accepts_every_work_order_target(TaskKind kind)
    {
        var pole = await PlantPole(.2);
        var unrelated = await PlantPole(.3);
        var id = await Plant(WorkOrderStatus.InProgress);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.TaskKind = kind;
            if (kind == TaskKind.Survey)
            {
                order.SegmentId = null;
                db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = home, SegmentId = segment, Position = 0 });
                // More than a client page of poles: membership must query Targets, not GET /poles page 1.
                var batch = Enumerable.Range(0, 201).Select(_ => new Pole { CommuneId = home, SegmentId = segment,
                    DataSource = DataSource.Simulated }).ToArray();
                db.AddRange(batch);
                await db.SaveChangesAsync();
                pole = batch.OrderBy(p => p.CreatedAt).ThenBy(p => p.PoleId.Length)
                    .ThenBy(p => p.PoleId, StringComparer.Ordinal).Last().PoleId;
            }
            if (kind == TaskKind.PoleGpsUpdate)
                db.Add(new WorkOrderPoleTarget { WorkOrderId = id, PoleId = pole, CommuneId = home });
            await db.SaveChangesAsync(); return 0;
        });
        if (kind == TaskKind.Survey)
        {
            var firstPage = await PolesOf(id, who: "a", query: "?page_size=200");
            Assert.Equal(203, firstPage.GetProperty("total").GetInt32());
            Assert.DoesNotContain(pole, firstPage.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("pole_id").GetString()));
        }
        var result = await Upload("a", id, kind == TaskKind.Repair ? "before" : "observation", poleId: pole);
        Assert.Equal(201, result.Status);
        Assert.Equal(pole, result.Body.GetProperty("pole_id").GetString());
        var list = await Send("manager", "GET", $"/{id}/evidence?pole_id={pole}", null, 200);
        Assert.Equal(pole, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("pole_id").GetString());
        if (kind == TaskKind.PoleGpsUpdate)
        {
            var rejected = await Upload("a", id, "observation", poleId: unrelated);
            Assert.Equal((409, "POLE_NOT_IN_WORK_ORDER"), (rejected.Status, Code(rejected.Body)));
        }
    }

    [Fact]
    public async Task Evidence_accepts_a_fault_target_without_a_road_or_geometry_and_does_not_publish()
    {
        var pole = await PlantPole(.2);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
            row.Geom = null; row.SegmentId = null;
            await db.SaveChangesAsync(); return 0;
        });
        var id = await Create("inspection", [await PlantPoleFault(pole, FaultStatus.Detected)], users["a"].UserId);
        await Send("a", "POST", $"/{id}/start", new { }, 200);
        var before = await Db(db => db.Set<LuxMap.Modules.Faults.Entities.Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole));
        var result = await Upload("a", id, "observation", poleId: pole);
        Assert.Equal(201, result.Status);
        Assert.False(await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().AnyAsync(p => p.PoleId == pole)));
        Assert.Equal(before, await Db(db => db.Set<LuxMap.Modules.Faults.Entities.Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Fact]
    public async Task Evidence_rejects_missing_out_of_scope_and_unrelated_poles_before_storage()
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        var outside = await PlantPole(.2, commune: foreign);
        var unrelated = await PlantPole(.3, road: foreignSegment);
        var writes = 0;
        photos.DuringWrite = () => { writes++; return Task.CompletedTask; };
        try
        {
            foreach (var (pole, status, code) in new[] { ("POLE-missing", 404, "POLE_NOT_FOUND"),
                (outside, 404, "POLE_NOT_FOUND"), (unrelated, 409, "POLE_NOT_IN_WORK_ORDER") })
            {
                var result = await Upload("a", id, "observation", poleId: pole);
                Assert.Equal((status, code), (result.Status, Code(result.Body)));
            }
            var hidden = await Upload("b", id, "observation", poleId: "POLE-missing");
            Assert.Equal((404, "WORK_ORDER_NOT_FOUND"), (hidden.Status, Code(hidden.Body)));
        }
        finally { photos.DuringWrite = null; }
        Assert.Equal(0, writes);
    }

    private sealed record EvidenceScope(CommuneScope Scope) : ICommuneScopeAccessor;

    [Fact]
    public async Task Evidence_requires_the_orders_commune_even_when_the_caller_sees_both()
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        var pole = await PlantPole(.2, commune: foreign); // Target on the same road, but another commune.
        // Synthetic scope isolates this invariant without creating an unsupported multi-commune account (ACCT-1).
        photoHost = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos));
            s.Replace(ServiceDescriptor.Scoped<ICommuneScopeAccessor>(_ => new EvidenceScope(CommuneScope.ForCommunes([home, foreign]))));
        }));
        var result = await Upload("a", id, "observation", poleId: pole);
        Assert.Equal((409, "POLE_NOT_IN_WORK_ORDER"), (result.Status, Code(result.Body)));
        Assert.Empty(photos.Objects);
        var own = await PlantPole(.3);
        var created = await Upload("a", id, "observation", poleId: own);
        Assert.Equal(201, created.Status);
        var evidence = created.Body.GetProperty("evidence_id").GetString()!;
        await Db(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"UPDATE repair_evidence SET pole_id = {pole} WHERE evidence_id = {evidence}"));
            Assert.Equal("23503", error.SqlState);
            Assert.Equal("fk_repair_evidence_pole_same_commune", error.ConstraintName);
            return 0;
        });
    }

    [Theory]
    [InlineData("move")]
    [InlineData("delete")]
    [InlineData("withdraw")]
    public async Task Evidence_rechecks_targets_after_storage(string change)
    {
        var pole = await PlantPole(.2);
        var id = await Plant(WorkOrderStatus.InProgress);
        photos.DuringWrite = () => Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
            if (change == "delete") db.Remove(row);
            else if (change == "move") row.SegmentId = foreignSegment;
            else
            {
                var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
                order.SegmentId = null;
            }
            await db.SaveChangesAsync(); return 0;
        });
        try
        {
            var result = await Upload("a", id, "observation", poleId: pole);
            Assert.Equal(change == "delete" ? (404, "POLE_NOT_FOUND") : (409, "POLE_NOT_IN_WORK_ORDER"), (result.Status, Code(result.Body)));
        }
        finally { photos.DuringWrite = null; }
        Assert.False(await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().AnyAsync(e => e.WorkOrderId == id)));
    }

    private Task<string> PlantEvidence(string id, string? pole, Guid key) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var row = new RepairEvidence { WorkOrderId = id, CommuneId = home, PoleId = pole, Kind = EvidenceKind.Observation,
            CapturedAt = DateTime.UtcNow, Lat = 16, Lng = 108, ObjectKey = "original/replay.jpg", ThumbnailKey = "thumb/replay.jpg",
            ByteCount = 1, ThumbnailBytes = 1, UploadedBy = users["a"].UserId, ClientOpId = key };
        db.Add(row); await db.SaveChangesAsync(); return row.EvidenceId;
    });

    // Simulate a competing commit becoming visible only after SaveChanges hits the unique index. Both pre-insert
    // replay reads see no row; the real DB raises 23505 and the rollback/replay branch must compare nullable pole too.
    private sealed class EvidenceReplayRace : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM repair_evidence", StringComparison.Ordinal)
                && command.CommandText.Contains("client_op_id =", StringComparison.Ordinal) && ++Reads <= 2)
                command.CommandText = command.CommandText.Replace("LIMIT 1", "LIMIT 0", StringComparison.Ordinal);
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("early", "different")] [InlineData("early", "null_to_pole")] [InlineData("early", "pole_to_null")]
    [InlineData("storage", "different")] [InlineData("storage", "null_to_pole")] [InlineData("storage", "pole_to_null")]
    [InlineData("unique", "different")] [InlineData("unique", "null_to_pole")] [InlineData("unique", "pole_to_null")]
    public async Task Evidence_replay_compares_nullable_pole_on_every_path(string path, string direction)
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        var first = await PlantPole(.2); var second = await PlantPole(.3);
        string? original = direction == "null_to_pole" ? null : first;
        string? requested = direction == "pole_to_null" ? null : second;
        var key = Guid.NewGuid();
        EvidenceReplayRace? race = null;
        if (path == "unique")
        {
            race = new EvidenceReplayRace();
            photoHost = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos));
                s.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(race));
            }));
        }
        if (path == "storage") photos.DuringWrite = async () => { await PlantEvidence(id, original, key); };
        else await PlantEvidence(id, original, key);
        try
        {
            var result = await Upload("a", id, "observation", clientOpId: key.ToString(), poleId: requested);
            Assert.Equal((409, "IDEMPOTENCY_CONFLICT"), (result.Status, Code(result.Body)));
            if (race is not null) Assert.Equal(3, race.Reads);
        }
        finally { photos.DuringWrite = null; }
        Assert.Equal(1, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(e => e.WorkOrderId == id)));
    }

    [Fact]
    public async Task Evidence_replays_the_same_pole_after_completion_and_target_changes()
    {
        var id = await Plant(WorkOrderStatus.InProgress); var pole = await PlantPole(.2); var key = Guid.NewGuid().ToString();
        var first = await Upload("a", id, "observation", clientOpId: key, poleId: pole);
        Assert.Equal(201, first.Status);
        await Send("a", "POST", $"/{id}/complete", new { report_note = "Inspected pole on site" }, 200);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
            row.SegmentId = null; await db.SaveChangesAsync(); return 0;
        });
        var replay = await Upload("a", id, "observation", clientOpId: key, poleId: pole);
        Assert.Equal(200, replay.Status);
        Assert.Equal(first.Body.GetRawText(), replay.Body.GetRawText());
    }

    [Fact]
    public async Task Evidence_filter_counts_before_pagination_and_keeps_capture_order()
    {
        var id = await Plant(WorkOrderStatus.InProgress); var first = await PlantPole(.2); var second = await PlantPole(.3);
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var upload = await Upload("a", id, "observation", capturedAt: $"2026-10-04T13:{i:D2}:00Z", poleId: i % 2 == 0 ? first : second);
            Assert.Equal(201, upload.Status);
            if (i % 2 == 0) ids.Add(upload.Body.GetProperty("evidence_id").GetString()!);
        }
        var overview = await Upload("a", id, "observation");
        Assert.Equal(201, overview.Status); Assert.Equal(JsonValueKind.Null, overview.Body.GetProperty("pole_id").ValueKind);
        var page = await Send("manager", "GET", $"/{id}/evidence?pole_id={first}&page=2&page_size=1", null, 200);
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(ids[1], Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("evidence_id").GetString());
        var empty = await Send("manager", "GET", $"/{id}/evidence?pole_id=POLE-missing", null, 200);
        Assert.Equal(0, empty.GetProperty("total").GetInt32()); Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(6, (await Send("manager", "GET", $"/{id}/evidence", null, 200)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Evidence_fk_prevents_pole_deletion_with_a_clean_asset_conflict()
    {
        var id = await Plant(WorkOrderStatus.InProgress); var pole = await PlantPole(.2);
        Assert.Equal(201, (await Upload("a", id, "observation", poleId: pole)).Status);
        var response = await clients["manager"].DeleteAsync($"/api/v1/assets/poles/{pole}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ASSET_IN_USE", Code(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement));
        Assert.True(await Db(db => db.Set<Pole>().IgnoreQueryFilters().AnyAsync(p => p.PoleId == pole)));
        Assert.Equal(1, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(e => e.WorkOrderId == id)));
    }

    private sealed class EvidenceLocks : DbCommandInterceptor
    {
        public readonly TaskCompletionSource PoleReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<string> Order = [];
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT 1 FROM work_order ", StringComparison.Ordinal)) Order.Add("order");
            if (command.CommandText.StartsWith("SELECT 1 FROM pole ", StringComparison.Ordinal)) { Order.Add("pole"); PoleReached.TrySetResult(); }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Evidence_holds_order_then_pole_locks_without_holding_them_during_storage()
    {
        var id = await Plant(WorkOrderStatus.InProgress); var pole = await PlantPole(.2);
        var locks = new EvidenceLocks();
        photoHost = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos));
            s.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(locks));
        }));
        // Warm host/client before taking locks; login must not affect the timing assertion.
        await PhotoClient("a");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var held = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {pole} FOR NO KEY UPDATE");
        photos.DuringWrite = () => { Assert.Empty(locks.Order); return Task.CompletedTask; };
        var upload = Upload("a", id, "observation", poleId: pole);
        try
        {
            await locks.PoleReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(300);
            Assert.False(upload.IsCompleted, "Upload bypassed the explicit pole lock");
            await using var probe = factory.Services.CreateAsyncScope();
            var probeDb = probe.ServiceProvider.GetRequiredService<LuxMapDbContext>();
            await using var probeTx = await probeDb.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => probeDb.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR NO KEY UPDATE NOWAIT"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
            await probeTx.RollbackAsync();
        }
        finally { photos.DuringWrite = null; await held.RollbackAsync(); }
        Assert.Equal(201, (await upload).Status);
        Assert.Equal(["order", "pole"], locks.Order);
    }
}
