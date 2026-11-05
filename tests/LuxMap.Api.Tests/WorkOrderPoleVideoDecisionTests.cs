using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-c3 (D-13): a Manager confirms a clip only after opening it themselves; rejecting needs no viewing.</summary>
public partial class WorkOrderTests
{
    private async Task<(int Status, JsonElement Body)> TryDecide(string order, string photo, string decision, string who = "manager")
    {
        var response = await (await PoleClient(who)).PostAsJsonAsync(PhotosPath(order) + $"/{photo}/decision", new { decision, note = "Đã kiểm tra" });
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string ErrorCode(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<(string Order, string Photo)> ClipPhoto(string label = "off")
    {
        poleHints.Result = new(label, label == "inconclusive" ? null : .9, "model-test-v1", "model-test-v1+cq-legacy");
        var (order, photo, _, _) = await NewPoleVideo();
        return (order, photo);
    }

    private async Task AssertUndecided(string order, string photo)
    {
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Null(row.Decision); Assert.False(row.StatusApplied);
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.OriginPolePhotoId == photo)));
        Assert.Equal(0, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.EntityId == order && e.Action == AuditAction.Confirmed)));
    }

    [Theory]
    [InlineData("off")] [InlineData("on")]
    public async Task A_clip_cannot_be_confirmed_before_the_deciding_manager_has_opened_it_and_can_after(string label)
    {
        var (order, photo) = await ClipPhoto(label);
        var refused = await TryDecide(order, photo, "confirm");
        Assert.Equal((409, "POLE_VIDEO_NOT_VIEWED"), (refused.Status, ErrorCode(refused.Body)));
        var details = refused.Body.GetProperty("error").GetProperty("details");
        Assert.Equal(photo, details.GetProperty("photo_id").GetString());
        Assert.EndsWith($"/pole-photos/{photo}/video", details.GetProperty("video_url").GetString());
        await AssertUndecided(order, photo);

        using (await GetVideo(order, photo, range: "bytes=0-9")) { }                // one range of the player is enough: it is a barrier, not proof
        var confirmed = await TryDecide(order, photo, "confirm");
        Assert.Equal(200, confirmed.Status);
        Assert.Equal("confirm", confirmed.Body.GetProperty("decision").GetString());
        Assert.True(confirmed.Body.GetProperty("status_applied").GetBoolean());
        var faults = await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.OriginPolePhotoId == photo));
        Assert.Equal(label == "off" ? 1 : 0, faults);                               // OFF raises the lamp_out the barrier protects against
    }

    [Fact]
    public async Task Rejecting_a_clip_needs_no_viewing()
    {
        var (order, photo) = await ClipPhoto();
        var rejected = await TryDecide(order, photo, "reject");
        Assert.Equal(200, rejected.Status);
        Assert.Equal("reject", rejected.Body.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task A_clip_someone_else_opened_does_not_count_for_the_manager_who_decides()
    {
        var (order, photo) = await ClipPhoto();
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo);
            row.VideoViewedBy = users["a"].UserId; row.VideoViewedAt = DateTime.UtcNow;       // the latest to open it is the engineer's colleague, not this Manager
            await db.SaveChangesAsync(); return 0;
        });
        var refused = await TryDecide(order, photo, "confirm");
        Assert.Equal((409, "POLE_VIDEO_NOT_VIEWED"), (refused.Status, ErrorCode(refused.Body)));
        using (await GetVideo(order, photo)) { }
        Assert.Equal(200, (await TryDecide(order, photo, "confirm")).Status);
    }

    [Fact]
    public async Task An_engineer_relabelling_the_clip_does_not_replace_the_Managers_viewing()
    {
        var (order, photo) = await ClipPhoto("off");
        var review = await (await PoleClient("a")).PatchAsJsonAsync(PhotosPath(order) + $"/{photo}/review", new { final_cv_state = "on", review_note = "Lamp is lit" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("POLE_VIDEO_NOT_VIEWED", ErrorCode((await TryDecide(order, photo, "confirm")).Body));
        using (await GetVideo(order, photo)) { }
        Assert.Equal(200, (await TryDecide(order, photo, "confirm")).Status);
    }

    [Fact]
    public async Task A_clip_with_no_verdict_is_refused_as_inconclusive_before_the_viewing_is_asked_for()
    {
        var (order, photo) = await ClipPhoto("inconclusive");
        var refused = await TryDecide(order, photo, "confirm");
        Assert.Equal((409, "POLE_PHOTO_INCONCLUSIVE"), (refused.Status, ErrorCode(refused.Body)));
    }

    [Fact]
    public async Task A_plain_photo_is_confirmed_exactly_as_before_without_any_viewing()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        Assert.Equal(200, (await TryDecide(id, photo, "confirm")).Status);
    }

    [Fact]
    public async Task Once_decided_a_second_confirmation_is_the_usual_conflict_not_a_viewing_error()
    {
        var (order, photo) = await ClipPhoto();
        using (await GetVideo(order, photo)) { }
        Assert.Equal(200, (await TryDecide(order, photo, "confirm")).Status);
        var again = await TryDecide(order, photo, "confirm");
        Assert.Equal((409, "POLE_PHOTO_ALREADY_DECIDED"), (again.Status, ErrorCode(again.Body)));
    }
}
