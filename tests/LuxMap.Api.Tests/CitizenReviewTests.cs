using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite.Geometries;
using Point = NetTopologySuite.Geometries.Point;

namespace LuxMap.Api.Tests;

/// <summary>CITIZEN-QR P2c — the Manager's queue: read, open photos, accept / merge / reject (D-5, D-9, D-11, D-17, D-18).</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class CitizenReviewTests(AssetImportFixture factory) : IAsyncLifetime
{
    private const string Queue = "/api/v1/citizen-reports";

    private readonly PhotoStore photos = new();
    private readonly CountingHinter hinter = new();
    private WebApplicationFactory<Program> host = null!;

    public Task InitializeAsync()
    {
        host = Host(services => services.Replace(ServiceDescriptor.Singleton<ICitizenPhotoHinter>(hinter)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task Accepting_makes_a_confirmed_field_report_fault_from_the_poles_data_and_the_report_points_at_it()
    {
        var (pole, report) = await FiledAsync("lamp_out", photo: true);
        var manager = await ManagerAsync();
        var managerId = await UserIdAsync(factory.ManagerUsername);

        var response = await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 1, fault_type = "lamp_out", severity = "high", note = "Đã xem ảnh" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await Read(response);
        Assert.Equal("accepted", detail.GetProperty("status").GetString());
        var faultId = detail.GetProperty("fault_id").GetString()!;
        Assert.Equal(managerId, detail.GetProperty("decided_by").GetString());
        Assert.Empty(detail.GetProperty("allowed_actions").EnumerateArray());

        var (fault, created) = await factory.QueryAsync(async db => (
            await db.Set<Fault>().IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.FaultId == faultId),
            await db.Set<CitizenReport>().IgnoreQueryFilters().AsNoTracking().Where(r => r.ReportId == report).Select(r => r.CreatedAt).SingleAsync()));
        Assert.Equal(pole, fault.PoleId);
        Assert.Equal(SourceChannel.FieldReport, fault.SourceChannel);
        Assert.Equal(FaultStatus.Confirmed, fault.FaultStatus);
        Assert.Equal(FaultType.LampOut, fault.FaultType);
        Assert.Equal(Severity.High, fault.Severity);
        Assert.Equal(DataSource.Field, fault.DataSource);
        Assert.Equal(managerId, fault.ReportedBy);
        Assert.Equal(managerId, fault.ConfirmedBy);
        Assert.Equal(created, fault.DetectedAt);
        Assert.Equal("Đã xem ảnh", fault.ReviewNote);
        Assert.Contains(report, fault.Note, StringComparison.Ordinal);

        // Two saves, one event each: the fault's creation and the report's acceptance.
        var events = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => (e.EntityType == AuditEntityType.Fault && e.EntityId == faultId)
                || (e.EntityType == AuditEntityType.CitizenReport && e.EntityId == report && e.Action != AuditAction.Submitted))
            .Select(e => new { e.EntityType, e.Action, e.ActorUserId }).ToListAsync());
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EntityType == AuditEntityType.Fault && e.Action == AuditAction.Created && e.ActorUserId == managerId);
        Assert.Contains(events, e => e.EntityType == AuditEntityType.CitizenReport && e.Action == AuditAction.Accepted && e.ActorUserId == managerId);
    }

    [Fact]
    public async Task A_report_is_decided_once()
    {
        var (_, report) = await FiledAsync("lamp_out");
        var manager = await ManagerAsync();
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "Đèn vẫn sáng khi kiểm tra" })).StatusCode);

        foreach (var (action, body) in new (string, object)[]
        {
            ("reject", new { submission_count = 1, reason = "lần hai" }), ("accept", new { submission_count = 1, fault_type = "lamp_out" }), ("merge", new { submission_count = 1, fault_id = "FAULT-0001" }),
        })
        {
            var again = await manager.PostAsJsonAsync($"{Queue}/{report}/{action}", body);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(ErrorCodes.CitizenReportAlreadyDecided, (await Read(again)).GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_report_where_nobody_said_the_lamp_is_out_or_dim_cannot_become_a_lamp_fault()
    {
        var (pole, report) = await FiledAsync("pole_damaged");
        var manager = await ManagerAsync();

        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));
        Assert.Equal(["reject"], detail.GetProperty("allowed_actions").EnumerateArray().Select(a => a.GetString()));

        var accept = await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 1, fault_type = "lamp_out" });
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal(ErrorCodes.CitizenReportNotAcceptable, (await Read(accept)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await factory.QueryAsync(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Fact]
    public async Task With_an_open_fault_of_that_type_on_the_pole_accept_is_refused_and_merge_attaches_without_touching_the_fault()
    {
        var (pole, report) = await FiledAsync("lamp_out");
        var open = await PlantFaultAsync(pole, FaultStatus.Detected);
        var before = await factory.QueryAsync(db => db.Set<Fault>().IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.FaultId == open));
        var manager = await ManagerAsync();

        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));
        Assert.Equal([open], detail.GetProperty("open_faults").EnumerateArray().Select(f => f.GetProperty("fault_id").GetString()));
        // accept is certain to be refused, so it is not offered (Codex review P2c #1).
        Assert.Equal(["merge", "reject"], detail.GetProperty("allowed_actions").EnumerateArray().Select(a => a.GetString()));
        Assert.Empty(detail.GetProperty("acceptable_fault_types").EnumerateArray());

        var accept = await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 1, fault_type = "lamp_out" });
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        var error = (await Read(accept)).GetProperty("error");
        Assert.Equal(ErrorCodes.OpenFaultExists, error.GetProperty("code").GetString());
        Assert.Equal([open], error.GetProperty("details").GetProperty("fault_ids").EnumerateArray().Select(f => f.GetString()));

        var merged = await Read(await manager.PostAsJsonAsync($"{Queue}/{report}/merge", new { submission_count = 1, fault_id = open }));
        Assert.Equal("merged", merged.GetProperty("status").GetString());
        Assert.Equal(open, merged.GetProperty("fault_id").GetString());

        var after = await factory.QueryAsync(db => db.Set<Fault>().IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.FaultId == open));
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.FaultStatus, after.FaultStatus);
    }

    [Fact]
    public async Task Merge_takes_only_an_open_fault_on_the_same_pole()
    {
        var (pole, report) = await FiledAsync("lamp_out");
        var (otherPole, _) = await FiledAsync("lamp_out");
        var closed = await PlantFaultAsync(pole, FaultStatus.Resolved);
        var elsewhere = await PlantFaultAsync(otherPole, FaultStatus.Confirmed);
        var manager = await ManagerAsync();

        foreach (var fault in new[] { closed, elsewhere })
        {
            var merge = await manager.PostAsJsonAsync($"{Queue}/{report}/merge", new { submission_count = 1, fault_id = fault });
            Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
            Assert.Equal(ErrorCodes.FaultNotMergeable, (await Read(merge)).GetProperty("error").GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{Queue}/{report}/merge", new { submission_count = 1, fault_id = "FAULT-999999999" })).StatusCode);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_keeps_it()
    {
        var (_, report) = await FiledAsync("other");
        var manager = await ManagerAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = new string('x', 1001) })).StatusCode);

        var rejected = await Read(await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "  Không phải đèn của xã  " }));
        Assert.Equal("rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Không phải đèn của xã", rejected.GetProperty("decision_note").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.GetProperty("fault_id").ValueKind);
    }

    [Fact]
    public async Task The_queue_shows_pending_by_default_newest_first_and_filters_by_status()
    {
        var (pole, first) = await FiledAsync("lamp_out", photo: true);
        var (_, second) = await FiledAsync("lamp_dim");
        var (_, decided) = await FiledAsync("other");
        var manager = await ManagerAsync();
        await manager.PostAsJsonAsync($"{Queue}/{decided}/reject", new { submission_count = 1, reason = "Không liên quan" });

        var pending = await Read(await manager.GetAsync($"{Queue}?page_size=200"));
        var ids = pending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("report_id").GetString()).ToList();
        Assert.DoesNotContain(decided, ids);
        Assert.True(ids.IndexOf(second) < ids.IndexOf(first), "newest send first");

        var item = pending.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("report_id").GetString() == first);
        Assert.Equal(pole, item.GetProperty("pole_id").GetString());
        Assert.Equal(1, item.GetProperty("submission_count").GetInt32());
        Assert.Equal(1, item.GetProperty("photo_count").GetInt32());
        Assert.Equal(["lamp_out"], item.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()));
        Assert.False(item.TryGetProperty("reporter_lat", out _));

        var rejected = await Read(await manager.GetAsync($"{Queue}?status=rejected&page_size=200"));
        Assert.Contains(decided, rejected.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("report_id").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{Queue}?status=closed")).StatusCode);
    }

    /// <summary>A reader without the review capability sees the metadata and the photo COUNT — never a link that would answer 403.</summary>
    [Fact]
    public async Task A_reader_who_cannot_review_sees_no_photo_links_no_actions_and_cannot_open_or_decide()
    {
        var (_, report) = await FiledAsync("lamp_out", photo: true);
        var photoId = await PhotoIdAsync(report);
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");

        var detail = await Read(await admin.GetAsync($"{Queue}/{report}"));
        Assert.Equal(1, detail.GetProperty("photo_count").GetInt32());
        var send = detail.GetProperty("submissions")[0];
        Assert.Equal(1, send.GetProperty("photo_count").GetInt32());
        Assert.Empty(send.GetProperty("photos").EnumerateArray());
        Assert.Empty(detail.GetProperty("allowed_actions").EnumerateArray());
        Assert.Equal(0, hinter.Calls);

        var photo = await admin.GetAsync($"{Queue}/{report}/photos/{photoId}/original");
        Assert.Equal(HttpStatusCode.Forbidden, photo.StatusCode);
        Assert.Equal(ErrorCodes.RoleForbidden, (await Read(photo)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task The_Manager_opens_the_photos_of_the_report_and_only_through_that_report()
    {
        var (_, report) = await FiledAsync("lamp_out", photo: true);
        var (_, other) = await FiledAsync("lamp_out");
        var photoId = await PhotoIdAsync(report);
        var manager = await ManagerAsync();

        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));
        var listed = detail.GetProperty("submissions")[0].GetProperty("photos")[0];
        Assert.Equal($"{Queue}/{report}/photos/{photoId}/thumbnail", listed.GetProperty("thumbnail_url").GetString());

        foreach (var url in new[] { listed.GetProperty("thumbnail_url").GetString()!, listed.GetProperty("original_url").GetString()! })
        {
            var image = await manager.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
            Assert.Equal([0xFF, 0xD8, 0xFF], (await image.Content.ReadAsByteArrayAsync())[..3]);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Queue}/{other}/photos/{photoId}/original")).StatusCode);
    }

    [Fact]
    public async Task A_report_of_another_commune_is_not_found_for_reading_photos_or_deciding()
    {
        var foreign = await PlantForeignReportAsync();
        var manager = await ManagerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{Queue}/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{Queue}/{foreign}/reject", new { submission_count = 1, reason = "Không phải" })).StatusCode);
        Assert.DoesNotContain(foreign, (await Read(await manager.GetAsync($"{Queue}?page_size=200"))).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("report_id").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.GetAsync($"{Queue}?commune_id={factory.ForeignCommuneId}")).StatusCode);
    }

    [Fact]
    public async Task The_hint_runs_once_per_photo_for_the_Manager_and_is_kept_with_its_model_version()
    {
        var (_, report) = await FiledAsync("lamp_out", photo: true);
        var manager = await ManagerAsync();

        var first = await Read(await manager.GetAsync($"{Queue}/{report}"));
        var second = await Read(await manager.GetAsync($"{Queue}/{report}"));

        Assert.Equal(1, hinter.Calls);
        foreach (var detail in new[] { first, second })
        {
            var hint = detail.GetProperty("submissions")[0].GetProperty("photos")[0].GetProperty("ai_hint");
            Assert.Equal("lamp_off", hint.GetProperty("label").GetString());
            Assert.Equal(0.8, hint.GetProperty("confidence").GetDouble(), 6);
            Assert.Equal(CountingHinter.Version, hint.GetProperty("artifact_version").GetString());
        }
    }

    [Fact]
    public async Task Without_a_model_the_report_still_opens_and_the_photo_has_no_hint()
    {
        hinter.Answer = null;
        var (_, report) = await FiledAsync("lamp_out", photo: true);

        var response = await (await ManagerAsync()).GetAsync($"{Queue}/{report}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Read(response)).GetProperty("submissions")[0].GetProperty("photos")[0].GetProperty("ai_hint").ValueKind);
    }

    /// <summary>
    /// The real model through the real hinter. What it SEES in a synthetic photo is not asserted (AI-1: accuracy is WP4's) — it called a
    /// black frame one dark lamp. The contract is: a known label, a confidence exactly when conclusive, and a version naming both the
    /// detector (BE-34) and the citizen rules.
    /// </summary>
    [Fact]
    public async Task The_real_model_gives_a_hint_with_the_detector_and_rules_version()
    {
        await using var real = Host(null);
        var (_, report) = await FiledAsync("lamp_out", photo: true, through: real);

        var detail = await Read(await (await ManagerAsync(real)).GetAsync($"{Queue}/{report}"));

        var hint = detail.GetProperty("submissions")[0].GetProperty("photos")[0].GetProperty("ai_hint");
        var label = hint.GetProperty("label").GetString();
        Assert.Contains(label, new[] { "lamp_on", "lamp_off", "inconclusive" });
        Assert.Equal(label == "inconclusive", hint.GetProperty("confidence").ValueKind == JsonValueKind.Null);
        Assert.Matches("^yolo-[0-9a-f]{16}\\+cq-[0-9a-f]{12}$", hint.GetProperty("artifact_version").GetString());
    }

    [Theory]
    [InlineData(new[] { "normal" }, 0.10, CitizenPhotoHint.LampOn)]
    [InlineData(new[] { "out" }, 0.10, CitizenPhotoHint.LampOff)]
    [InlineData(new[] { "out" }, 0.60, CitizenPhotoHint.Inconclusive)]
    [InlineData(new[] { "normal", "normal" }, 0.10, CitizenPhotoHint.Inconclusive)]
    [InlineData(new[] { "normal", "out" }, 0.10, CitizenPhotoHint.Inconclusive)]
    [InlineData(new string[0], 0.10, CitizenPhotoHint.Inconclusive)]
    public void A_photo_says_lit_or_dark_only_for_one_lamp_in_a_dark_photo(string[] classes, double luma, CitizenPhotoHint expected)
    {
        var detections = classes.Select(c => new DetectionResult { ClassName = c, Confidence = 0.7f }).ToList();

        var (label, confidence) = YoloCitizenPhotoHinter.Classify(detections, luma, daylightLuma: 0.45);

        Assert.Equal(expected, label);
        Assert.Equal(expected == CitizenPhotoHint.Inconclusive, confidence is null);
    }

    [Fact]
    public async Task A_bright_thumbnail_reads_as_daylight_and_a_dark_one_does_not()
    {
        static MemoryStream Shade(byte value)
        {
            using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(320, 240,
                new SixLabors.ImageSharp.PixelFormats.Rgb24(value, value, value));
            var output = new MemoryStream();
            SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(image, output);
            return output;
        }

        Assert.True(await YoloCitizenPhotoHinter.MeanLumaAsync(Shade(230), CancellationToken.None) > 0.45);
        Assert.True(await YoloCitizenPhotoHinter.MeanLumaAsync(Shade(20), CancellationToken.None) < 0.45);
    }

    /// <summary>A Manager decides on what they read: a send that arrived after they opened the report makes the decision a 409.</summary>
    [Fact]
    public async Task A_decision_on_a_stale_count_of_sends_is_refused_until_the_report_is_reloaded()
    {
        var (_, report) = await FiledAsync("lamp_out");
        var manager = await ManagerAsync();

        var stale = await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 2, reason = "Không phải sự cố" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var error = (await Read(stale)).GetProperty("error");
        Assert.Equal(ErrorCodes.CitizenReportChanged, error.GetProperty("code").GetString());
        Assert.Equal(1, error.GetProperty("details").GetProperty("submission_count").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { reason = "Không phải sự cố" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "Không phải sự cố" })).StatusCode);
    }

    /// <summary>D-17: "out" backs lamp_out, "dim" backs lamp_dim — a Manager does not file a different fault than any citizen reported.</summary>
    [Fact]
    public async Task Accept_takes_only_a_fault_type_a_citizen_reported()
    {
        var (_, report) = await FiledAsync("lamp_out");
        var manager = await ManagerAsync();

        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));
        Assert.Equal(["lamp_out"], detail.GetProperty("acceptable_fault_types").EnumerateArray().Select(t => t.GetString()));

        var dim = await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 1, fault_type = "lamp_dim" });
        Assert.Equal(HttpStatusCode.Conflict, dim.StatusCode);
        Assert.Equal(ErrorCodes.CitizenReportNotAcceptable, (await Read(dim)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 1, fault_type = "node_offline" })).StatusCode);
    }

    /// <summary>Two openings at once may both run the model, but the hint is written once, with one <c>analyzed</c> event by <c>cv</c>.</summary>
    [Fact]
    public async Task The_hint_is_an_audited_automated_finding_written_once()
    {
        var (_, report) = await FiledAsync("lamp_out", photo: true);
        var manager = await ManagerAsync();

        var openings = await Task.WhenAll(manager.GetAsync($"{Queue}/{report}"), manager.GetAsync($"{Queue}/{report}"));
        Assert.All(openings, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var events = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.EntityType == AuditEntityType.CitizenReport && e.EntityId == report && e.Action == AuditAction.Analyzed).ToListAsync());
        var analyzed = Assert.Single(events);
        Assert.Equal(AuditActorKind.Cv, analyzed.ActorKind);
        Assert.Null(analyzed.ActorUserId);
        Assert.Contains(CountingHinter.Version, analyzed.AfterState, StringComparison.Ordinal);
    }

    /// <summary>
    /// A decision locks the POLE row — the lock a public send holds while it files into the report. Without it a send could join a
    /// report decided a moment earlier and never reach a Manager. Held with <c>FOR NO KEY UPDATE</c> so nothing else blocks.
    /// </summary>
    [Fact]
    public async Task A_decision_waits_for_the_pole_lock_a_public_send_holds()
    {
        var (pole, report) = await FiledAsync("lamp_out");
        var manager = await ManagerAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {pole} FOR NO KEY UPDATE");

        var decision = manager.PostAsJsonAsync($"{Queue}/{report}/reject", new { submission_count = 1, reason = "Không phải sự cố" });
        await Task.WhenAny(decision, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(decision.IsCompleted, "the decision finished while the pole row was locked — it never took the lock");

        await transaction.RollbackAsync();
        Assert.Equal(HttpStatusCode.OK, (await decision).StatusCode);
    }

    [Fact]
    public async Task A_mixed_report_offers_only_the_type_not_already_open_on_the_pole()
    {
        var (pole, report, code) = await FiledWithCodeAsync("lamp_out", 0, host);
        await SendAsync(host, code, "lamp_dim");
        await PlantFaultAsync(pole, FaultStatus.Confirmed);
        var manager = await ManagerAsync();

        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));

        Assert.Equal(["lamp_dim"], detail.GetProperty("acceptable_fault_types").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["accept", "merge", "reject"], detail.GetProperty("allowed_actions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(HttpStatusCode.OK,
            (await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 2, fault_type = "lamp_dim" })).StatusCode);
    }

    /// <summary>A slow send can be received before the one that opened the report: the fault is detected at the EARLIEST send.</summary>
    [Fact]
    public async Task The_fault_is_detected_at_the_earliest_send_not_when_the_report_opened()
    {
        var (_, report) = await FiledAsync("lamp_out");
        var earlier = await factory.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var first = await db.Set<CitizenSubmission>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.ReportId == report);
            var received = first.ReceivedAt.AddMinutes(-7);
            db.Add(new CitizenSubmission
            {
                ReportId = first.ReportId, QrId = first.QrId, PoleId = first.PoleId, CommuneId = first.CommuneId, ClientOpId = Guid.NewGuid(),
                RequestHash = new string('a', 64), Kind = CitizenReportKind.LampOut, ReceivedAt = received,
            });
            await db.SaveChangesAsync();
            return received;
        });
        var manager = await ManagerAsync();

        var accepted = await Read(await manager.PostAsJsonAsync($"{Queue}/{report}/accept", new { submission_count = 2, fault_type = "lamp_out" }));

        var faultId = accepted.GetProperty("fault_id").GetString();
        Assert.Equal(earlier, await factory.QueryAsync(db => db.Set<Fault>().IgnoreQueryFilters()
            .Where(f => f.FaultId == faultId).Select(f => f.DetectedAt).SingleAsync()));
    }

    [Fact]
    public async Task One_opening_runs_the_model_on_at_most_HintsPerRequest_photos()
    {
        await using var capped = Host(services => services.Replace(ServiceDescriptor.Singleton<ICitizenPhotoHinter>(hinter)), ("Citizen:HintsPerRequest", "2"));
        var (_, report) = await FiledAsync("lamp_out", photos: 3, through: capped);
        var manager = await ManagerAsync(capped);

        await manager.GetAsync($"{Queue}/{report}");
        Assert.Equal(2, hinter.Calls);
        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));
        Assert.Equal(3, hinter.Calls);
        Assert.All(detail.GetProperty("submissions")[0].GetProperty("photos").EnumerateArray(),
            p => Assert.NotEqual(JsonValueKind.Null, p.GetProperty("ai_hint").ValueKind));
    }

    /// <summary>A photo the model could not read waits out a cooldown, so it does not take the next opening's slot (Codex review P2c #3).</summary>
    [Fact]
    public async Task A_photo_that_could_not_be_hinted_does_not_starve_the_photos_after_it()
    {
        hinter.ByCall = call => call == 1 ? null : new PhotoHint(CitizenPhotoHint.LampOn, 0.9, CountingHinter.Version);
        await using var capped = Host(services => services.Replace(ServiceDescriptor.Singleton<ICitizenPhotoHinter>(hinter)), ("Citizen:HintsPerRequest", "1"));
        var (_, report) = await FiledAsync("lamp_out", photos: 2, through: capped);
        var manager = await ManagerAsync(capped);

        await manager.GetAsync($"{Queue}/{report}");
        var detail = await Read(await manager.GetAsync($"{Queue}/{report}"));

        var hints = detail.GetProperty("submissions")[0].GetProperty("photos").EnumerateArray()
            .Select(p => p.GetProperty("ai_hint").ValueKind).ToList();
        Assert.Equal([JsonValueKind.Null, JsonValueKind.Object], hints);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private sealed class CountingHinter : ICitizenPhotoHinter
    {
        public const string Version = "test-hinter-1";

        private int calls;

        public int Calls => calls;

        public PhotoHint? Answer { get; set; } = new(CitizenPhotoHint.LampOff, 0.8, Version);

        /// <summary>When set, the answer by call number (1-based) — to fail one call and not the next.</summary>
        public Func<int, PhotoHint?>? ByCall { get; set; }

        public Task<PhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref calls);
            return Task.FromResult(ByCall is { } byCall ? byCall(call) : Answer);
        }
    }

    private WebApplicationFactory<Program> Host(Action<IServiceCollection>? configure, params (string Key, string Value)[] settings)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:PublicLookup:PermitLimit", "10000");
            builder.UseSetting("RateLimits:PublicReport:PermitLimit", "10000");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos));
                configure?.Invoke(services);
            });
        });

    private async Task<HttpClient> ManagerAsync(WebApplicationFactory<Program>? through = null)
    {
        var client = (through ?? host).CreateClient();
        var token = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = factory.ManagerUsername, password = factory.AccountPassword });
        var access = (await Read(token)).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        return client;
    }

    /// <summary>A fresh pole with a code, and one public send about it — the report that send opened.</summary>
    private async Task<(string Pole, string Report)> FiledAsync(
        string kind, bool photo = false, WebApplicationFactory<Program>? through = null, int photos = 0)
    {
        var (pole, report, _) = await FiledWithCodeAsync(kind, photo ? Math.Max(photos, 1) : photos, through ?? host);
        return (pole, report);
    }

    private async Task<(string Pole, string Report, string Code)> FiledWithCodeAsync(string kind, int photos, WebApplicationFactory<Program> server)
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var issued = await Read(await (await ManagerAsync(server)).PostAsJsonAsync("/api/v1/qr-codes", new { pole_id = pole }));
        var code = issued.GetProperty("code").GetString()!;
        await SendAsync(server, code, kind, photos);

        var report = await factory.QueryAsync(db => db.Set<CitizenReport>().IgnoreQueryFilters()
            .Where(r => r.PoleId == pole).Select(r => r.ReportId).SingleAsync());
        return (pole, report, code);
    }

    private static async Task SendAsync(WebApplicationFactory<Program> server, string code, string kind, int photos = 0)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(code), "code" },
            { new StringContent(kind), "kind" },
            { new StringContent(Guid.NewGuid().ToString()), "client_op_id" },
        };
        for (var i = 0; i < photos; i++) form.Add(new ByteArrayContent(PhotoStore.Jpeg((byte)(40 + i))), "photos", $"photo{i}.jpg");
        var sent = await server.CreateClient().PostAsync("/api/v1/public/reports", form);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
    }

    private Task<string> PlantPoleAsync(string commune) => factory.QueryAsync(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var road = new RoadSegment
        {
            CommuneId = commune, SegmentName = "Review road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Field,
            Geom = new LineString([new Coordinate(107.5, 11.4), new Coordinate(107.51, 11.41)]) { SRID = 4326 }, LengthM = 100,
        };
        db.Add(road);
        await db.SaveChangesAsync();
        var pole = new Pole { CommuneId = commune, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.505, 11.405) { SRID = 4326 } };
        db.Add(pole);
        await db.SaveChangesAsync();
        return pole.PoleId;
    });

    private Task<string> PlantFaultAsync(string pole, FaultStatus status) => factory.QueryAsync(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var now = DateTime.UtcNow;
        var fault = new Fault
        {
            PoleId = pole, CommuneId = factory.CommuneId, FaultType = FaultType.LampOut, FaultStatus = status, Severity = Severity.Medium,
            SourceChannel = SourceChannel.Cv, DataSource = DataSource.Field, DetectedAt = now.AddDays(-1), CreatedAt = now, UpdatedAt = now,
        };
        db.Add(fault);
        await db.SaveChangesAsync();
        return fault.FaultId;
    });

    private Task<string> PlantForeignReportAsync() => factory.QueryAsync(async db =>
    {
        var pole = await PlantPoleAsync(factory.ForeignCommuneId);
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var now = DateTime.UtcNow;
        var report = new CitizenReport
        {
            PoleId = pole, CommuneId = factory.ForeignCommuneId, DataSource = DataSource.Field, NightOf = DateOnly.FromDateTime(now),
            Status = CitizenReportStatus.Pending, CreatedAt = now, LastSubmittedAt = now,
        };
        db.Add(report);
        await db.SaveChangesAsync();
        return report.ReportId;
    });

    private Task<string> PhotoIdAsync(string report) => factory.QueryAsync(db => db.Set<CitizenPhoto>()
        .Join(db.Set<CitizenSubmission>().IgnoreQueryFilters(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => new { p.PhotoId, s.ReportId })
        .Where(x => x.ReportId == report).Select(x => x.PhotoId).SingleAsync());

    private Task<string> UserIdAsync(string username) => factory.QueryAsync(db => db.Database
        .SqlQuery<string>($"SELECT user_id AS \"Value\" FROM app_user WHERE username = {username}").SingleAsync());

    private static async Task<JsonElement> Read(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
