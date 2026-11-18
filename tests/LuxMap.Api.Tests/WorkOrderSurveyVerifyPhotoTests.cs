using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private Task MakeSurveyForPhotoVerify(string id)
        => Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.TaskKind = TaskKind.Survey; order.SegmentId = null;
            db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = home, SegmentId = segment, Position = 0 });
            await db.SaveChangesAsync(); return 0;
        });

    [Fact]
    public async Task Survey_verify_lists_only_live_undecided_photos_and_clips_then_succeeds_after_decisions()
    {
        var (id, clip, _, _) = await NewPoleVideo();
        await MakeSurveyForPhotoVerify(id);
        var pole = await PlantPole(.3);
        // Capture order deliberately differs from upload/ID order; no hard-coded IDs or sequence mutation.
        var photo = await NewPolePhoto(id, pole, "2026-10-04T14:00:00Z");
        var rejected = await NewPolePhoto(id, pole);
        var withdrawn = await NewPolePhoto(id, pole);
        await PoleDecision(id, rejected, "reject");
        await PoleWithdraw(id, withdrawn);
        await Send("a", "POST", $"/{id}/complete", new { report_note = "Đã hoàn thành khảo sát" }, 200);
        var before = await Send("manager", "GET", $"/{id}", null, 200);
        Assert.Contains("verify", before.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));

        var refused = await Send("manager", "POST", $"/{id}/verify", new { note = "Nghiệm thu khảo sát" },
            409, "POLE_PHOTOS_UNDECIDED");
        Assert.Equal([photo, clip], refused.GetProperty("error").GetProperty("details").GetProperty("photo_ids")
            .EnumerateArray().Select(x => x.GetString()!).ToArray());
        var saved = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id));
        Assert.Equal(WorkOrderStatus.Done, saved.WoStatus);
        Assert.Null(saved.ClosedAt); Assert.Null(saved.ReviewNote);
        Assert.Equal(before.GetProperty("completed_at").GetDateTime(), saved.CompletedAt);
        Assert.Equal(0, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .CountAsync(e => e.EntityId == id && e.Action == AuditAction.Verified)));

        await PoleDecision(id, photo);
        await PoleDecision(id, clip, "reject");
        var verified = await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Equal("verified", verified.GetProperty("wo_status").GetString());
    }

    [Fact]
    public async Task Survey_verify_without_pole_photos_succeeds()
    {
        var id = await Plant(WorkOrderStatus.Done);
        await MakeSurveyForPhotoVerify(id);
        var verified = await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Equal("verified", verified.GetProperty("wo_status").GetString());
    }

    [Theory]
    [InlineData(TaskKind.Inspection)]
    [InlineData(TaskKind.Repair)]
    public async Task Non_survey_verify_still_accepts_undecided_pole_photos(TaskKind kind)
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        await NewPolePhoto(id, await PlantPole(.2));
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.TaskKind = kind; order.WoStatus = WorkOrderStatus.Done;
            order.CompletedAt = DateTime.UtcNow; order.ReportNote = "Planted completed report";
            await db.SaveChangesAsync(); return 0;
        });
        var verified = await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Equal("verified", verified.GetProperty("wo_status").GetString());
    }

    [Fact]
    public async Task Survey_verify_checks_role_and_state_before_undecided_photos()
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        await MakeSurveyForPhotoVerify(id);
        await NewPolePhoto(id, await PlantPole(.2));
        await Send("a", "POST", $"/{id}/verify", new { }, 403, "ROLE_FORBIDDEN");
        await Send("manager", "POST", $"/{id}/verify", new { }, 409, "INVALID_STATE_TRANSITION");
    }
}
