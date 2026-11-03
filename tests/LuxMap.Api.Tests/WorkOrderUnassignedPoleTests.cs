using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private Task<string> UnassignedPole(string? explicitId = null, DateTime? created = null) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var pole = new Pole
        {
            CommuneId = home, DataSource = DataSource.Simulated,
            Geom = new Point(108.005, 16.005) { SRID = 4326 },
        };
        if (explicitId is not null) pole.PoleId = explicitId;
        if (created is { } stamp) pole.CreatedAt = stamp;
        db.Add(pole);
        await db.SaveChangesAsync();
        return pole.PoleId;
    });

    [Fact]
    public async Task Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last()
    {
        // Fixed width boundary, asserted free. No sequence is moved; the collection serializes asset writers.
        string[] boundary = ["POLE-9999", "POLE-10000"];
        var reserved = boundary.Append("POLE-10001").ToArray();
        Assert.Empty(await Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => reserved.Contains(p.PoleId)).ToArrayAsync()));
        var unrelated = await UnassignedPole();
        var older = await UnassignedPole(explicitId: "POLE-10001", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            await using var tx = await db.Database.BeginTransactionAsync();
            // now() is the transaction clock: same created_at, opposite insertion / expected ID order.
            foreach (var id in boundary.Reverse())
                db.Add(new Pole { PoleId = id, CommuneId = home, DataSource = DataSource.Simulated,
                    Geom = new Point(108.005, 16.005) { SRID = 4326 } });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Assert.Single(await db.Set<Pole>().IgnoreQueryFilters().Where(p => boundary.Contains(p.PoleId))
                .Select(p => p.CreatedAt).Distinct().ToArrayAsync());
            return 0;
        });
        var faults = new List<string>();
        foreach (var pole in boundary.Append(older)) faults.Add(await PlantPoleFault(pole, FaultStatus.Detected));
        var order = await Create(faults: faults.ToArray(), assigned: users["a"].UserId);
        Assert.Null(await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().Where(w => w.WorkOrderId == order).Select(w => w.SegmentId).SingleAsync()));
        // Keep the order's anchor null, but add a fault target with a road to prove the unassigned group is LAST.
        var onRoad = await PlantPole(.8);
        var roadFault = await PlantPoleFault(onRoad, FaultStatus.Detected);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new WorkOrderFault { WorkOrderId = order, FaultId = roadFault, CommuneId = home });
            await db.SaveChangesAsync();
            return 0;
        });
        var all = await PolesOf(order);
        var items = all.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { onRoad, older }.Concat(boundary), items.Select(p => p.GetProperty("pole_id").GetString()));
        Assert.DoesNotContain(items, p => p.GetProperty("pole_id").GetString() == unrelated);
        Assert.Equal(1, items[0].GetProperty("position").GetInt32());
        foreach (var row in items.Skip(1))
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("segment_id").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("position").ValueKind);
            Assert.Single(row.GetProperty("work_order_fault_ids").EnumerateArray());
        }
        var page = await PolesOf(order, query: "?page=2&page_size=2");
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(boundary, page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("pole_id").GetString()));
        // The agenda must keep a fault-only order, even with no road to group it under.
        var agenda = await clients["a"].GetFromJsonAsync<JsonElement>(Route + "/agenda");
        Assert.Contains(order, agenda.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_order_pole_photo_uses_the_same_unassigned_target_policy()
    {
        var target = await UnassignedPole();
        var unrelated = await UnassignedPole();
        var fault = await PlantPoleFault(target, FaultStatus.Detected);
        var order = await Create(faults: [fault], assigned: users["a"].UserId);
        await Send("a", "POST", $"/{order}/start", null, 200);
        var refused = await PoleUpload(order, unrelated);
        Assert.Equal(409, refused.Status);
        Assert.Equal("POLE_NOT_IN_WORK_ORDER", Code(refused.Body));
        Assert.Empty(poleStore.Objects);
        await PoleClient();
        var inferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        poleHints.DuringInference = () => { inferred.TrySetResult(); return Task.CompletedTask; };
        await Db(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {target} FOR NO KEY UPDATE");
            var upload = PoleUpload(order, target);
            try
            {
                await inferred.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(600);
                Assert.False(upload.IsCompleted, "Upload bypassed the target pole lock");
            }
            finally { await tx.RollbackAsync(); }
            var result = await upload;
            Assert.True(result.Status == 201, result.Body.GetRawText());
            return 0;
        });
        poleHints.DuringInference = null;
        var before = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == order));
        poleStore.DuringWrite = async () => await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            await db.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == target);
            pole.SegmentId = segment;
            await db.SaveChangesAsync();
            return 0;
        });
        var changed = await PoleUpload(order, target);
        Assert.Equal(409, changed.Status);
        Assert.Equal("POLE_PHOTO_TARGET_CHANGED", Code(changed.Body));
        Assert.Equal(before, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == order)));
        poleStore.DuringWrite = async () => await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var link = await db.Set<WorkOrderFault>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == order && w.FaultId == fault);
            db.Remove(link); await db.SaveChangesAsync(); return 0;
        });
        var removed = await PoleUpload(order, target);
        Assert.Equal(409, removed.Status);
        Assert.Equal("POLE_NOT_IN_WORK_ORDER", Code(removed.Body));
        Assert.Equal(before, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == order)));
    }

    [Fact]
    public async Task Fault_on_unassigned_pole_keeps_pole_location_commune_and_source()
    {
        var pole = await UnassignedPole();
        var operation = Guid.NewGuid();
        var request = new { pole_id = pole, client_op_id = operation, fault_type = "lamp_dim", note = "Đèn yếu khi kiểm tra tại chỗ" };
        var response = await clients["a"].PostAsJsonAsync("/api/v1/faults", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reported = await response.Content.ReadFromJsonAsync<JsonElement>();
        var faultId = reported.GetProperty("fault_id").GetString()!;
        Assert.Equal(JsonValueKind.Null, reported.GetProperty("segment_id").ValueKind);
        Assert.Equal(16.005, reported.GetProperty("location").GetProperty("lat").GetDouble());
        Assert.Equal(108.005, reported.GetProperty("location").GetProperty("lng").GetDouble());
        var replay = await clients["a"].PostAsJsonAsync("/api/v1/faults", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(reported.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        var order = await Create(faults: [faultId], assigned: users["a"].UserId);
        await Send("a", "POST", $"/{order}/start", null, 200);
        var photo = await NewPolePhoto(order, pole);
        await PoleDecision(order, photo);
        var faults = await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.PoleId == pole).ToArrayAsync());
        Assert.Equal(2, faults.Length);
        foreach (var fault in faults)
        {
            Assert.Null(fault.SegmentId);
            Assert.Equal(home, fault.CommuneId);
            Assert.Equal(DataSource.Simulated, fault.DataSource);
            var audit = Assert.Single(await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
                .Where(a => a.EntityType == AuditEntityType.Fault && a.EntityId == fault.FaultId).ToArrayAsync()));
            Assert.Equal(AuditAction.Created, audit.Action);
            Assert.Equal(home, audit.CommuneId);
        }
        var published = Assert.Single(faults, f => f.OriginPolePhotoId == photo);
        Assert.Equal(SourceChannel.Cv, published.SourceChannel);
        Assert.Equal(FaultStatus.Confirmed, published.FaultStatus);
        Assert.Equal(SourceChannel.FieldReport, faults.Single(f => f.FaultId == faultId).SourceChannel);
        // D-7: assigning the asset later must not rewrite either event.
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
            row.SegmentId = segment; await db.SaveChangesAsync(); return 0;
        });
        Assert.All(await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.PoleId == pole).ToArrayAsync()), f => Assert.Null(f.SegmentId));
    }

    [Fact]
    public async Task Survey_never_targets_unassigned_poles()
    {
        var unassigned = await UnassignedPole();
        var assigned = await PlantPole(.5); // Same point: proximity cannot substitute for membership.
        var created = await Send("manager", "POST", "", new { task_kind = "survey", title = "Khảo sát theo tuyến",
            commune_id = home, segment_ids = new[] { segment }, assigned_to = users["a"].UserId }, 201);
        var order = created.GetProperty("work_order_id").GetString()!;
        var list = (await PolesOf(order)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(assigned, Assert.Single(list).GetProperty("pole_id").GetString());
        await Db(async db =>
        {
            var snapshot = await SurveyChainageQuery.Snapshot(db, "SWEEP-no-samples-p2b", [segment], [home], "simulated",
                new SurveyProcessingOptions(), CancellationToken.None);
            Assert.Empty(snapshot.Gps);
            Assert.Equal(assigned, Assert.Single(snapshot.Poles).PoleId);
            Assert.DoesNotContain(snapshot.Poles, p => p.PoleId == unassigned);
            return 0;
        });
        await Send("manager", "POST", "", new { task_kind = "survey", title = "Không có tuyến", commune_id = home,
            fault_ids = new[] { await PlantPoleFault(unassigned, FaultStatus.Detected) } }, 400, "VALIDATION_FAILED");
    }
}
