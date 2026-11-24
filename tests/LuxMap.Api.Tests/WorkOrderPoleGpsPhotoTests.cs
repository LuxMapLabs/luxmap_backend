using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Modules.WorkOrders.Jobs;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>POLE-GPS-PHOTO (drift PGP-1…PGP-8) — a photo of every reported pole, and verified photos copied into the poles' galleries.</summary>
public partial class WorkOrderTests
{
    /// <summary>One <c>observation</c> photo per pole, tied to it, as the "Chụp ảnh cột" button sends them.</summary>
    private async Task<string[]> Photograph(string id, params string[] poleIds)
    {
        var ids = new List<string>();
        foreach (var poleId in poleIds)
        {
            var (status, body) = await Upload("a", id, "observation", poleId: poleId);
            Assert.Equal(201, status);
            ids.Add(body.GetProperty("evidence_id").GetString()!);
        }
        return [.. ids];
    }

    private async Task<string> Photo(string id, string poleId, byte shade, string capturedAt)
    {
        var (status, body) = await Upload("a", id, "observation", Jpeg(shade), capturedAt, poleId: poleId);
        Assert.Equal(201, status);
        return body.GetProperty("evidence_id").GetString()!;
    }

    private async Task<string> CompletedGps(string[] poleIds, Func<string, Task> photograph)
    {
        var id = await CreateGps(poleIds);
        await Send("a", "POST", $"/{id}/start", null, 200);
        await photograph(id);
        await Send("a", "POST", $"/{id}/complete", new
        {
            report_note = "Đã dán tem QR, lấy GPS và chụp ảnh cột.",
            // Off where each pole was planted, so every pole really moves and gets its `relocated` event.
            pole_locations = poleIds.Select((p, i) => Location(p, 16.0021 + i * .002, 108.0021 + i * .002)).ToArray(),
        }, 200);
        return id;
    }

    private async Task<string> VerifiedGps(string[] poleIds, Func<string, Task> photograph)
    {
        var id = await CompletedGps(poleIds, photograph);
        await Send("manager", "POST", $"/{id}/verify", new { note = "Toạ độ và ảnh hợp lý." }, 200, auditExpected: 1 + poleIds.Length);
        return id;
    }

    private async Task<string> RunPhotoCopy()
    {
        await PhotoClient("a");
        var job = photoHost!.Services.GetServices<IScheduledJob>().OfType<PolePhotoCopyJob>().Single();
        var correlation = "job:pole_photo_copy:" + Guid.NewGuid().ToString("N");
        await job.RunAsync(UtcMicrosecondClock.UtcNow(), correlation, default);
        return correlation;
    }

    private Task<List<WorkOrderPhotoCopy>> Ledger(string id)
        => Db(db => db.Set<WorkOrderPhotoCopy>().IgnoreQueryFilters().Where(x => x.WorkOrderId == id).ToListAsync());

    private Task<List<PoleAssetPhoto>> Gallery(string poleId)
        => Db(db => db.Set<PoleAssetPhoto>().IgnoreQueryFilters().Where(x => x.PoleId == poleId && x.DeletedAt == null).ToListAsync());

    private static string ObjectKeyOf(string evidenceId) => StorageKeys.KeyFor(ObjectVariant.Original, evidenceId);

    [Fact]
    public async Task Complete_needs_a_photo_tied_to_every_pole_it_reports()
    {
        var one = await PlantPole(.2);
        var two = await PlantPole(.4);
        var id = await CreateGps([one, two]);
        await Send("a", "POST", $"/{id}/start", null, 200);
        const string note = "Đã lấy toạ độ các cột.";
        var both = new[] { Location(two, 16.004, 108.004), Location(one, 16.002, 108.002) };

        var refused = await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = both }, 409, "POLE_EVIDENCE_REQUIRED");
        var details = refused.GetProperty("error").GetProperty("details");
        Assert.Equal(id, details.GetProperty("work_order_id").GetString());
        // In the order's own pole order, whatever order the phone sent them in.
        Assert.Equal([one, two], details.GetProperty("pole_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());

        // A photo tied to no pole proves no pole.
        Assert.Equal(201, (await Upload("a", id, "observation")).Status);
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = both }, 409, "POLE_EVIDENCE_REQUIRED");

        await Photograph(id, one);
        refused = await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = both }, 409, "POLE_EVIDENCE_REQUIRED");
        Assert.Equal([two], refused.GetProperty("error").GetProperty("details").GetProperty("pole_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal("in_progress", (await Send("manager", "GET", $"/{id}", null, 200)).GetProperty("wo_status").GetString());

        // A pole not reported needs no photo.
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.002, 108.002) } }, 200);
        // A photo of the round before a return still counts.
        await Send("manager", "POST", $"/{id}/return", new { note = "Đo lại cột một." }, 200);
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.0021, 108.0021) } }, 200);
    }

    [Fact]
    public async Task Sync_push_answers_a_missing_pole_photo_as_a_conflict()
    {
        var pole = await PlantPole(.2);
        var id = await CreateGps([pole]);
        await Send("a", "POST", $"/{id}/start", null, 200);
        var response = await clients["a"].PostAsJsonAsync("/api/v1/sync/push", new
        {
            operations = new[]
            {
                new Dictionary<string, object?>
                {
                    ["client_op_id"] = Guid.NewGuid(), ["op_type"] = "work_order_complete",
                    ["payload"] = new { work_order_id = id, report_note = "Đã lấy toạ độ cột trên tuyến.", pole_locations = new[] { Location(pole, 16.002, 108.002) } },
                },
            },
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var pushed = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Empty(pushed.GetProperty("applied").EnumerateArray());
        Assert.Equal("POLE_EVIDENCE_REQUIRED", Assert.Single(pushed.GetProperty("conflicts").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal(WorkOrderStatus.InProgress, await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters()
            .Where(w => w.WorkOrderId == id).Select(w => w.WoStatus).SingleAsync()));
        Assert.Null(await Db(db => db.Set<WorkOrderPoleTarget>().IgnoreQueryFilters()
            .Where(t => t.WorkOrderId == id).Select(t => t.ReportedLat).SingleAsync()));
    }

    [Fact]
    public async Task Only_a_verified_order_has_its_photos_copied_into_the_pole_galleries_once()
    {
        var one = await PlantPole(.2);
        var two = await PlantPole(.4);
        string[] evidence = [];
        var id = await CompletedGps([one, two], async order =>
        {
            evidence = [await Photo(order, one, 70, "2026-11-20T01:00:00Z"), await Photo(order, one, 80, "2026-11-20T01:05:00Z"),
                await Photo(order, two, 90, "2026-11-20T01:10:00Z")];
            Assert.Equal(201, (await Upload("a", order, "observation", Jpeg(100))).Status); // tied to no pole: never copied
        });

        // Done is still waiting on the manager: nothing is copied yet.
        await RunPhotoCopy();
        Assert.Empty(await Ledger(id));
        Assert.Empty(await Gallery(one));

        await Send("manager", "POST", $"/{id}/verify", new { note = "Toạ độ và ảnh hợp lý." }, 200, auditExpected: 3);
        // Verify itself copies nothing: the job does, after it commits (PGP-3).
        Assert.Empty(await Gallery(one));

        var correlation = await RunPhotoCopy();
        var ledger = await Ledger(id);
        Assert.Equal(evidence.Order().ToArray(), ledger.Select(x => x.EvidenceId).Order().ToArray());
        Assert.All(ledger, x => Assert.Equal(PhotoCopyOutcome.Copied, x.Outcome));
        Assert.Equal(2, (await Gallery(one)).Count);
        var copied = Assert.Single(await Gallery(two));
        var source = ledger.Single(x => x.PoleId == two);
        Assert.Equal(copied.PhotoId, source.PhotoId);
        Assert.Equal(users["a"].UserId, copied.UploadedBy);
        Assert.Equal(PolePhotoCopyJob.ClientOpId(source.EvidenceId), copied.ClientOpId);
        // Its own object, byte for byte the engineer's original (D-1) — never the evidence key.
        Assert.Equal(StorageKeys.KeyFor(ObjectVariant.Original, copied.PhotoId), copied.ObjectKey);
        Assert.Equal(photos.Objects[(StorageBucket.Evidence, ObjectKeyOf(source.EvidenceId))], photos.Objects[(StorageBucket.Evidence, copied.ObjectKey)]);

        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.CorrelationId == correlation).ToListAsync());
        Assert.Equal(3, events.Count);
        Assert.All(events, x => Assert.Equal((AuditEntityType.PoleAssetPhoto, AuditAction.Created, AuditActorKind.System), (x.EntityType, x.Action, x.ActorKind)));
        var after = JsonDocument.Parse(events.Single(x => x.EntityId == copied.PhotoId).AfterState!).RootElement;
        Assert.Equal(source.EvidenceId, after.GetProperty("evidence_id").GetString());
        Assert.Equal(id, after.GetProperty("work_order_id").GetString());

        // Handled once: a second run copies nothing more and writes no audit.
        var again = await RunPhotoCopy();
        Assert.Equal(2, (await Gallery(one)).Count);
        Assert.Equal(3, (await Ledger(id)).Count);
        Assert.Empty(await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.CorrelationId == again).ToListAsync()));

        // The copied photos hold their pole like any gallery photo: deleting the pole is refused and rolled back whole.
        var deleted = await clients["manager"].DeleteAsync($"/api/v1/assets/poles/{one}");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal(2, (await Gallery(one)).Count);
    }

    [Fact]
    public async Task A_nearly_full_gallery_takes_the_newest_photo_and_the_rest_are_recorded_as_skipped()
    {
        var pole = await PlantPole(.2);
        for (var i = 0; i < 9; i++) await UploadGalleryPhoto(pole, (byte)(10 + i));
        string oldest = "", middle = "", newest = "";
        var id = await VerifiedGps([pole], async order =>
        {
            middle = await Photo(order, pole, 71, "2026-11-20T01:05:00Z");
            newest = await Photo(order, pole, 72, "2026-11-20T01:10:00Z");
            oldest = await Photo(order, pole, 73, "2026-11-20T01:00:00Z");
        });

        await RunPhotoCopy();
        var ledger = (await Ledger(id)).ToDictionary(x => x.EvidenceId);
        Assert.Equal(PhotoCopyOutcome.Copied, ledger[newest].Outcome);
        Assert.Equal(PhotoCopyOutcome.SkippedLimit, ledger[middle].Outcome);
        Assert.Equal(PhotoCopyOutcome.SkippedLimit, ledger[oldest].Outcome);
        Assert.Null(ledger[middle].PhotoId);
        Assert.Equal(10, (await Gallery(pole)).Count);
    }

    private async Task UploadGalleryPhoto(string poleId, byte shade)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Jpeg(shade));
        file.Headers.ContentType = new("image/jpeg");
        form.Add(file, "photo", "pole.jpg");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "client_op_id");
        var response = await (await PhotoClient("manager")).PostAsync($"/api/v1/assets/poles/{poleId}/photos", form);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_gallery_filled_while_the_job_writes_the_object_is_counted_again_under_the_pole_lock()
    {
        var pole = await PlantPole(.2);
        for (var i = 0; i < 9; i++) await UploadGalleryPhoto(pole, (byte)(20 + i));
        string evidence = "";
        var id = await VerifiedGps([pole], async order => evidence = await Photo(order, pole, 76, "2026-11-20T01:00:00Z"));

        // The job saw one free slot before writing; a manager takes it while the object is being written.
        photos.DuringWrite = async () => { photos.DuringWrite = null; await UploadGalleryPhoto(pole, 30); };
        try { await RunPhotoCopy(); }
        finally { photos.DuringWrite = null; }

        var row = Assert.Single(await Ledger(id));
        Assert.Equal((evidence, PhotoCopyOutcome.SkippedLimit), (row.EvidenceId, row.Outcome));
        Assert.Equal(10, (await Gallery(pole)).Count);
    }

    [Fact]
    public async Task A_photo_that_is_gone_or_above_the_pixel_cap_is_recorded_and_never_copied()
    {
        var pole = await PlantPole(.2);
        string gone = "", huge = "";
        var id = await VerifiedGps([pole], async order =>
        {
            gone = await Photo(order, pole, 74, "2026-11-20T01:00:00Z");
            huge = await Photo(order, pole, 75, "2026-11-20T01:05:00Z");
        });
        photos.Objects.TryRemove((StorageBucket.Evidence, ObjectKeyOf(gone)), out _);
        // Evidence has no pixel cap; the gallery's is 40 MP. A header that says 8000 × 6000 is enough: the reader stops before decoding.
        photos.Objects[(StorageBucket.Evidence, ObjectKeyOf(huge))] = WithSize(Jpeg(75), 8000, 6000);

        await RunPhotoCopy();
        var ledger = (await Ledger(id)).ToDictionary(x => x.EvidenceId);
        Assert.Equal(PhotoCopyOutcome.MissingObject, ledger[gone].Outcome);
        Assert.Equal(PhotoCopyOutcome.SkippedPixels, ledger[huge].Outcome);
        Assert.Empty(await Gallery(pole));
    }

    /// <summary>The JPEG with its baseline frame header (SOF0) claiming another size; the scan data is left as it was.</summary>
    private static byte[] WithSize(byte[] jpeg, int width, int height)
    {
        var copy = (byte[])jpeg.Clone();
        for (var i = 2; i < copy.Length - 9; i++)
        {
            if (copy[i] != 0xFF || copy[i + 1] != 0xC0) continue;
            copy[i + 5] = (byte)(height >> 8); copy[i + 6] = (byte)height;
            copy[i + 7] = (byte)(width >> 8); copy[i + 8] = (byte)width;
            return copy;
        }
        throw new InvalidOperationException("No baseline frame header.");
    }
}
