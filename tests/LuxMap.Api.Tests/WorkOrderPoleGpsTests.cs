using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>WO-POLE-GPS (drift PG-1…PG-13) — label chosen poles, take their GPS, and only a manager's verify writes it to the GIS.</summary>
public partial class WorkOrderTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n% LuxMap test labels\n%%EOF\n"u8.ToArray();

    private static string Captured => DateTime.UtcNow.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private async Task<string> CreateGps(string[] poleIds, string? road = null, int expected = 201, string? error = null)
    {
        var json = await Send("manager", "POST", "", new
        {
            task_kind = "pole_gps_update", title = "Dán tem QR và lấy GPS", segment_id = road ?? segment, pole_ids = poleIds,
            assigned_to = users["a"].UserId,
        }, expected, error);
        return expected == 201 ? json.GetProperty("work_order_id").GetString()! : "";
    }

    private static object Location(string poleId, double lat, double lng, double accuracy = 1.8)
        => new { pole_id = poleId, lat, lng, accuracy_m = accuracy, captured_at = Captured };

    private Task<Point?> GeomOf(string poleId)
        => Db(db => db.Set<Pole>().IgnoreQueryFilters().Where(x => x.PoleId == poleId).Select(x => x.Geom).SingleAsync());

    private Task<string> SecondHomeRoad() => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var road = new RoadSegment
        {
            CommuneId = home, SegmentName = "WO test second road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
            Geom = new LineString([new Coordinate(108.1, 16.1), new Coordinate(108.11, 16.11)]) { SRID = 4326 }, LengthM = 100,
        };
        db.Add(road);
        await db.SaveChangesAsync();
        return road.SegmentId;
    });

    [Fact]
    public async Task A_pole_gps_order_names_its_poles_and_its_pole_list_is_only_those()
    {
        var located = await PlantPole(.2);
        var unlocated = await UnlocatedPole();
        var bystander = await PlantPole(.5); // same road, not chosen

        var id = await CreateGps([unlocated, located]);

        var detail = await Send("manager", "GET", $"/{id}", null, 200);
        Assert.Equal("pole_gps_update", detail.GetProperty("task_kind").GetString());
        Assert.Equal(segment, detail.GetProperty("segment_id").GetString());
        Assert.Equal([unlocated, located], detail.GetProperty("pole_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Empty(detail.GetProperty("fault_ids").EnumerateArray());
        Assert.Empty(detail.GetProperty("pole_locations").EnumerateArray());
        Assert.DoesNotContain("follow_up", detail.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));

        var item = (await Send("manager", "GET", $"?task_kind=pole_gps_update&page_size=200", null, 200)).GetProperty("items")
            .EnumerateArray().Single(x => x.GetProperty("work_order_id").GetString() == id);
        Assert.Equal([unlocated, located], item.GetProperty("pole_ids").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var poles = (await PolesOf(id, query: "?page_size=200")).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("pole_id").GetString()).ToArray();
        Assert.Equal(2, poles.Length);
        Assert.DoesNotContain(bystander, poles);
        Assert.Contains(located, poles);
        Assert.Contains(unlocated, poles);
    }

    [Fact]
    public async Task A_pole_gps_order_refuses_poles_off_its_road_poles_already_taken_faults_and_follow_ups()
    {
        var mine = await PlantPole(.3);
        var elsewhere = await PlantPole(.3, road: await SecondHomeRoad());

        var refused = await Send("manager", "POST", "", new
        {
            task_kind = "pole_gps_update", title = "Off road", segment_id = segment, pole_ids = new[] { mine, elsewhere },
        }, 400, "VALIDATION_FAILED");
        var details = refused.GetProperty("error").GetProperty("details");
        Assert.Equal("pole_ids", details.GetProperty("field").GetString());
        Assert.Equal([elsewhere], details.GetProperty("pole_ids_not_on_segment").EnumerateArray().Select(x => x.GetString()!).ToArray());

        await Send("manager", "POST", "", new { task_kind = "pole_gps_update", title = "No poles", segment_id = segment, pole_ids = Array.Empty<string>() }, 400, "VALIDATION_FAILED");
        await Send("manager", "POST", "", new { task_kind = "pole_gps_update", title = "With faults", segment_id = segment, pole_ids = new[] { mine }, fault_ids = new[] { "FAULT-0001" } }, 400, "VALIDATION_FAILED");
        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Poles elsewhere", segment_id = segment, pole_ids = new[] { mine } }, 400, "VALIDATION_FAILED");

        var first = await CreateGps([mine]);
        var taken = await CreateGps([mine], expected: 409, error: "POLE_ALREADY_IN_WORK_ORDER");
        Assert.Empty(taken);
        var conflict = (await Send("manager", "POST", "", new { task_kind = "pole_gps_update", title = "Again", segment_id = segment, pole_ids = new[] { mine } }, 409))
            .GetProperty("error").GetProperty("details");
        Assert.Equal(first, conflict.GetProperty("work_order_id").GetString());

        await Send("manager", "PATCH", $"/{first}", new { pole_ids = new[] { mine } }, 400, "VALIDATION_FAILED");

        // Single stage: never a parent, never a child (PG-1).
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var wo = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == first);
            wo.WoStatus = WorkOrderStatus.Verified;
            wo.StartedAt = wo.CompletedAt = wo.ClosedAt = DateTime.UtcNow;
            wo.ReportNote = "Planted report";
            await db.SaveChangesAsync();
            return 0;
        });
        var fromGps = await Send("manager", "POST", $"/{first}/follow-up", new { task_kind = "inspection" }, 409, "INVALID_STATE_TRANSITION");
        Assert.Empty(fromGps.GetProperty("error").GetProperty("details").GetProperty("allowed_follow_up_kinds").EnumerateArray());
        var inspection = await Plant(WorkOrderStatus.Verified);
        await Send("manager", "POST", $"/{inspection}/follow-up", new { task_kind = "pole_gps_update" }, 409, "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task Complete_keeps_the_report_on_the_order_and_only_verify_writes_it_to_the_gis()
    {
        var located = await PlantPole(.2);
        var unlocated = await UnlocatedPole();
        var before = await GeomOf(located);
        var id = await CreateGps([unlocated, located]);
        await Send("a", "POST", $"/{id}/start", null, 200);

        var done = await Send("a", "POST", $"/{id}/complete", new
        {
            report_note = "Đã dán tem QR và lấy GPS hai cột.",
            pole_locations = new[] { Location(located, 16.0021, 108.0021, 1.5), Location(unlocated, 16.006, 108.006) },
        }, 200);
        Assert.Equal("done", done.GetProperty("wo_status").GetString());
        // In pole_ids order, whatever order the phone sent them in.
        var reported = done.GetProperty("pole_locations").EnumerateArray().ToArray();
        Assert.Equal([unlocated, located], reported.Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        Assert.Equal(1.5, reported[1].GetProperty("accuracy_m").GetDouble());
        // Not in the GIS yet.
        Assert.Null(await GeomOf(unlocated));
        Assert.Equal(before!.X, (await GeomOf(located))!.X);

        var correlation = Guid.NewGuid().ToString();
        // One audit for the order and one `relocated` for each pole that moved (PL-4), in one request.
        var verified = await Send("manager", "POST", $"/{id}/verify", new { note = "Toạ độ hợp lý." }, 200, auditExpected: 3, correlation: correlation);
        Assert.Equal("verified", verified.GetProperty("wo_status").GetString());
        var placed = await GeomOf(unlocated);
        Assert.Equal(108.006, placed!.X, 9);
        Assert.Equal(16.006, placed.Y, 9);
        Assert.Equal(108.0021, (await GeomOf(located))!.X, 9);

        var relocations = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(x => x.CorrelationId == correlation && x.EntityType == AuditEntityType.Pole).ToListAsync());
        Assert.Equal(2, relocations.Count);
        Assert.All(relocations, x => Assert.Equal(AuditAction.Relocated, x.Action));
        Assert.All(relocations, x => Assert.Equal(id, JsonDocument.Parse(x.AfterState!).RootElement.GetProperty("work_order_id").GetString()));

        // Verified releases the poles: a new order may take them again (PG-3).
        await CreateGps([located]);
    }

    [Fact]
    public async Task A_report_outside_the_road_corridor_moves_no_pole_and_leaves_the_order_done()
    {
        var near = await PlantPole(.2);
        var unlocated = await UnlocatedPole();
        var before = await GeomOf(near);
        var id = await CreateGps([near, unlocated]);
        await Send("a", "POST", $"/{id}/start", null, 200);
        await Send("a", "POST", $"/{id}/complete", new
        {
            report_note = "Một cột đo lệch xa tuyến.",
            pole_locations = new[] { Location(near, 16.0021, 108.0021), Location(unlocated, 16.5, 108.5) },
        }, 200);

        var refused = await Send("manager", "POST", $"/{id}/verify", new { note = "Duyệt" }, 409, "POLE_OFF_SEGMENT");
        var details = refused.GetProperty("error").GetProperty("details");
        Assert.Equal(id, details.GetProperty("work_order_id").GetString());
        var off = details.GetProperty("poles").EnumerateArray().ToArray();
        Assert.Equal([unlocated], off.Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        Assert.True(off[0].GetProperty("distance_m").GetDouble() > off[0].GetProperty("corridor_m").GetDouble());

        // All or nothing: the pole within its corridor did not move either.
        Assert.Equal(before!.X, (await GeomOf(near))!.X);
        Assert.Null(await GeomOf(unlocated));
        Assert.Equal("done", (await Send("manager", "GET", $"/{id}", null, 200)).GetProperty("wo_status").GetString());
        await Send("manager", "POST", $"/{id}/return", new { note = "Đo lại cột lệch." }, 200);
    }

    [Fact]
    public async Task Complete_validates_the_report_and_a_second_complete_replaces_the_first()
    {
        var one = await PlantPole(.2);
        var two = await PlantPole(.4);
        var stranger = await PlantPole(.6);
        var id = await CreateGps([one, two]);
        await Send("a", "POST", $"/{id}/start", null, 200);
        const string note = "Đã lấy toạ độ các cột.";

        await Send("a", "POST", $"/{id}/complete", new { report_note = note }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = Array.Empty<object>() }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(stranger, 16.006, 108.006) } }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.002, 108.002), Location(one, 16.002, 108.002) } }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.002, 108.002, accuracy: -1) } }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 91, 108.002) } }, 400, "VALIDATION_FAILED");
        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { new { pole_id = one, lat = 16.002, lng = 108.002, accuracy_m = 2.0, captured_at = DateTime.UtcNow.AddHours(1).ToString("O") } } }, 400, "VALIDATION_FAILED");

        await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.002, 108.002), Location(two, 16.004, 108.004) } }, 200);
        // Return keeps the report for the engineer to read (PG-11) …
        var returned = await Send("manager", "POST", $"/{id}/return", new { note = "Đo lại cột hai." }, 200);
        Assert.Equal(2, returned.GetProperty("pole_locations").GetArrayLength());
        // … and the next complete replaces it as a whole: a pole not sent again loses its old report.
        var again = await Send("a", "POST", $"/{id}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.0021, 108.0021) } }, 200);
        Assert.Equal([one], again.GetProperty("pole_locations").EnumerateArray().Select(x => x.GetProperty("pole_id").GetString()!).ToArray());

        // Other kinds take no positions.
        var inspection = await Plant(WorkOrderStatus.InProgress);
        await Send("a", "POST", $"/{inspection}/complete", new { report_note = note, pole_locations = new[] { Location(one, 16.002, 108.002) } }, 400, "VALIDATION_FAILED");
    }

    // ---- PDF attachments (PG-6…PG-9) ---------------------------------------------------------------

    private async Task<(int Status, JsonElement Body)> Attach(string id, byte[] bytes, string name = "Danh_sach_tem_QR.pdf", string who = "manager")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        var response = await (await PhotoClient(who)).PostAsync($"{Route}/{id}/attachments", form);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<int> AttachmentRows(string id) => Db(db => db.Set<WorkOrderAttachment>().IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == id));

    [Fact]
    public async Task A_manager_attaches_a_pdf_that_the_assigned_engineer_downloads_and_no_one_else_sees()
    {
        var pole = await PlantPole(.2);
        var id = await CreateGps([pole]);

        var (status, body) = await Attach(id, Pdf, "C:\\Users\\x\\Tem_QR_4_cot.pdf");
        Assert.Equal(201, status);
        var attachmentId = body.GetProperty("attachment_id").GetString()!;
        Assert.StartsWith("ATT-", attachmentId);
        Assert.Equal("Tem_QR_4_cot.pdf", body.GetProperty("file_name").GetString());
        Assert.Equal(Pdf.Length, body.GetProperty("byte_count").GetInt64());
        Assert.Equal($"/api/v1/work-orders/{id}/attachments/{attachmentId}", body.GetProperty("download_url").GetString());

        var listed = (await Send("a", "GET", $"/{id}", null, 200)).GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal(attachmentId, listed.GetProperty("attachment_id").GetString());

        var download = await (await PhotoClient("a")).GetAsync($"{Route}/{id}/attachments/{attachmentId}");
        Assert.Equal(200, (int)download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());

        // Another engineer cannot see the order, so not its file either.
        Assert.Equal(404, (int)(await (await PhotoClient("b")).GetAsync($"{Route}/{id}/attachments/{attachmentId}")).StatusCode);
        // An engineer cannot attach.
        Assert.Equal(403, (await Attach(id, Pdf, who: "a")).Status);

        // Soft removal: gone from the order and the download, the row stays.
        Assert.Equal(204, (int)(await (await PhotoClient("manager")).DeleteAsync($"{Route}/{id}/attachments/{attachmentId}")).StatusCode);
        Assert.Empty((await Send("manager", "GET", $"/{id}", null, 200)).GetProperty("attachments").EnumerateArray());
        var gone = await (await PhotoClient("manager")).GetAsync($"{Route}/{id}/attachments/{attachmentId}");
        Assert.Equal(404, (int)gone.StatusCode);
        Assert.Equal(1, await AttachmentRows(id));
    }

    [Fact]
    public async Task An_attachment_must_be_a_pdf_by_its_bytes_on_an_open_pole_gps_order()
    {
        var pole = await PlantPole(.2);
        var id = await CreateGps([pole]);

        // A JPEG named .pdf and sent as application/pdf is still not a PDF.
        var (status, body) = await Attach(id, Jpeg(), "renamed.pdf");
        Assert.Equal(415, status);
        Assert.Equal("UNSUPPORTED_MEDIA_TYPE", Code(body));
        Assert.Equal(0, await AttachmentRows(id));

        var inspection = await Plant(WorkOrderStatus.Assigned);
        var (kindStatus, kindBody) = await Attach(inspection, Pdf);
        Assert.Equal(409, kindStatus);
        Assert.Equal("ATTACHMENT_NOT_ALLOWED", Code(kindBody));

        for (var i = 0; i < 10; i++) Assert.Equal(201, (await Attach(id, Pdf)).Status);
        var (fullStatus, fullBody) = await Attach(id, Pdf);
        Assert.Equal(409, fullStatus);
        Assert.Equal("ATTACHMENT_LIMIT_REACHED", Code(fullBody));

        await Send("manager", "POST", $"/{id}/cancel", new { note = "Huỷ lịch." }, 200);
        var (closedStatus, closedBody) = await Attach(id, Pdf);
        Assert.Equal(409, closedStatus);
        Assert.Equal("INVALID_STATE_TRANSITION", Code(closedBody));
    }
}
