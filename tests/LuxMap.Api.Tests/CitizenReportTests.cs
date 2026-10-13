using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite.Geometries;
using Point = NetTopologySuite.Geometries.Point;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Api.Tests;

/// <summary>
/// CITIZEN-QR P2b — the public form: no token, a code instead (D-R1, D-1…D-16, review P1 #1–#5).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class CitizenReportTests(AssetImportFixture factory) : IAsyncLifetime
{
    private const string Lookup = "/api/v1/public/qr/lookup";
    private const string Reports = "/api/v1/public/reports";

    private readonly PhotoStore photos = new();
    private WebApplicationFactory<Program> host = null!;

    public Task InitializeAsync()
    {
        host = Host();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task A_citizen_without_an_account_files_a_report_into_the_queue_never_a_fault()
    {
        var (pole, code) = await PoleWithCodeAsync();
        var anonymous = host.CreateClient();

        var response = await Submit(anonymous, code, "lamp_out", description: "Đèn tắt từ tối qua", photos: [Exif()]);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = await Read(response);
        Assert.Equal(1, receipt.GetProperty("photos_kept").GetInt32());
        Assert.Equal(["client_op_id", "photos_kept", "received_at"], receipt.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

        var report = await factory.QueryAsync(db => db.Set<CitizenReport>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.PoleId == pole));
        Assert.Equal(CitizenReportStatus.Pending, report.Status);
        Assert.Equal(factory.CommuneId, report.CommuneId);
        Assert.Null(report.FaultId);
        Assert.Equal(0, await factory.QueryAsync(db => db.Set<LuxMap.Modules.Faults.Entities.Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));

        var audit = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.EntityType == AuditEntityType.CitizenReport && e.EntityId == report.ReportId));
        Assert.Equal(AuditActorKind.Public, audit.ActorKind);
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.ActorRole);
        Assert.DoesNotContain(code, $"{audit.BeforeState}|{audit.AfterState}|{audit.Note}", StringComparison.Ordinal);
        Assert.DoesNotContain("Đèn tắt từ tối qua", audit.AfterState ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_send_for_the_same_pole_and_night_joins_the_item_keeps_its_own_content_and_notifies_once()
    {
        var (pole, code) = await PoleWithCodeAsync();
        var anonymous = host.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(anonymous, code, "lamp_out", description: "first")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(anonymous, code, "pole_damaged", description: "second")).StatusCode);

        var reports = await factory.QueryAsync(db => db.Set<CitizenReport>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.PoleId == pole).Select(r => r.ReportId).ToListAsync());
        var reportId = Assert.Single(reports);
        var sends = await factory.QueryAsync(db => db.Set<CitizenSubmission>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ReportId == reportId).OrderBy(s => s.ReceivedAt).Select(s => new { s.Kind, s.Description }).ToListAsync());
        Assert.Equal([(CitizenReportKind.LampOut, "first"), (CitizenReportKind.PoleDamaged, "second")], sends.Select(s => (s.Kind, s.Description!)));

        var notices = await factory.QueryAsync(db => db.Set<Notification>().IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.EntityId == reportId && n.Type == NotificationType.CitizenReportReceived).Select(n => n.RecipientUserId).ToListAsync());
        Assert.NotEmpty(notices);
        Assert.Equal(notices.Count, notices.Distinct().Count());
    }

    [Fact]
    public async Task Malformed_unknown_and_revoked_codes_get_the_same_404()
    {
        var (_, code) = await PoleWithCodeAsync();
        var manager = await factory.ManagerClientAsync();
        var qrId = (await Read(await manager.GetAsync($"/api/v1/qr-codes?active=true&page_size=200")))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("qr_id").GetString();
        await manager.PostAsync($"/api/v1/qr-codes/{qrId}/revoke", null);
        var anonymous = host.CreateClient();

        var bodies = new List<string>();
        foreach (var candidate in new[] { "short", "0000000000000000", code })
        {
            var lookup = await anonymous.PostAsJsonAsync(Lookup, new { code = candidate });
            Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
            var error = (await Read(lookup)).GetProperty("error");
            Assert.Equal(ErrorCodes.PublicQrNotFound, error.GetProperty("code").GetString());
            bodies.Add(error.GetProperty("message").GetString()!);

            var send = await Submit(anonymous, candidate, "lamp_out");
            Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task The_lookup_names_the_road_and_the_commune_and_no_internal_id()
    {
        var (pole, code) = await PoleWithCodeAsync();

        var body = await Read(await host.CreateClient().PostAsJsonAsync(Lookup, new { code }));

        Assert.Equal(["commune_name", "segment_name"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Citizen road", body.GetProperty("segment_name").GetString());
        Assert.DoesNotContain(pole, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_send_replays_and_the_key_under_other_content_or_another_code_is_409()
    {
        var (_, code) = await PoleWithCodeAsync();
        var (_, otherCode) = await PoleWithCodeAsync();
        var anonymous = host.CreateClient();
        var key = Guid.NewGuid();

        var first = await Read(await Submit(anonymous, code, "lamp_dim", key: key, description: "same"));
        var again = await Submit(anonymous, code, "lamp_dim", key: key, description: "same");
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(first.GetProperty("received_at").GetString(), (await Read(again)).GetProperty("received_at").GetString());

        foreach (var different in new[] { await Submit(anonymous, code, "lamp_dim", key: key, description: "changed"),
                     await Submit(anonymous, otherCode, "lamp_dim", key: key, description: "same") })
        {
            Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
            Assert.Equal(ErrorCodes.IdempotencyConflict, (await Read(different)).GetProperty("error").GetProperty("code").GetString());
        }

        Assert.Equal(1, await factory.QueryAsync(db => db.Set<CitizenSubmission>().IgnoreQueryFilters().CountAsync(s => s.ClientOpId == key)));
    }

    /// <summary>The sender's phone and position must not survive in what is stored (D-10) — original or thumbnail.</summary>
    [Fact]
    public async Task Stored_photos_carry_no_exif_and_no_gps()
    {
        var (pole, code) = await PoleWithCodeAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(host.CreateClient(), code, "lamp_out", photos: [Exif()])).StatusCode);

        var photoId = await factory.QueryAsync(db => db.Set<CitizenPhoto>().AsNoTracking()
            .Join(db.Set<CitizenSubmission>().IgnoreQueryFilters(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => new { p.PhotoId, s.PoleId })
            .Where(x => x.PoleId == pole).Select(x => x.PhotoId).SingleAsync());
        foreach (var variant in new[] { ObjectVariant.Original, ObjectVariant.Thumbnail })
        {
            var bytes = photos.Objects[(StorageBucket.Citizen, StorageKeys.KeyFor(variant, photoId))];
            using var image = Image.Load(bytes);
            Assert.Null(image.Metadata.ExifProfile);
        }
    }

    [Fact]
    public async Task A_photo_over_the_pixel_cap_is_refused_from_its_header_and_nothing_is_written()
    {
        await using var strict = Host(("Citizen:PhotoMaxPixels", "10000"));
        var (pole, code) = await PoleWithCodeAsync();

        var response = await Submit(strict.CreateClient(), code, "lamp_out", photos: [PhotoStore.Jpeg()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await Read(response)).GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Equal(10000, error.GetProperty("details").GetProperty("max_pixels").GetInt64());
        Assert.Equal(0, await factory.QueryAsync(db => db.Set<CitizenReport>().IgnoreQueryFilters().CountAsync(r => r.PoleId == pole)));
    }

    [Fact]
    public async Task A_file_that_is_not_a_jpeg_is_415()
    {
        var (_, code) = await PoleWithCodeAsync();

        var response = await Submit(host.CreateClient(), code, "lamp_out", photos: [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>Whoever holds a code cannot use up its allowance to silence everyone else: past it, the text still goes in (review P1 #5).</summary>
    [Fact]
    public async Task Past_the_codes_photo_allowance_the_text_is_kept_and_the_photos_dropped()
    {
        await using var tight = Host(("Citizen:PhotosPerCodePerNight", "1"));
        var (pole, code) = await PoleWithCodeAsync();
        var anonymous = tight.CreateClient();

        Assert.Equal(1, (await Read(await Submit(anonymous, code, "lamp_out", photos: [PhotoStore.Jpeg(10)]))).GetProperty("photos_kept").GetInt32());
        var second = await Submit(anonymous, code, "lamp_out", description: "still dark", photos: [PhotoStore.Jpeg(20)]);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(0, (await Read(second)).GetProperty("photos_kept").GetInt32());
        var dropped = await factory.QueryAsync(db => db.Set<CitizenSubmission>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.PoleId == pole && s.Description == "still dark"));
        Assert.Equal(PhotosDroppedReason.OverQuota, dropped.PhotosDropped);
    }

    [Fact]
    public async Task A_position_far_from_the_pole_is_flagged_not_refused()
    {
        var (pole, code) = await PoleWithCodeAsync();

        var response = await Submit(host.CreateClient(), code, "other", lat: 10.0, lng: 106.0);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(await factory.QueryAsync(db => db.Set<CitizenSubmission>().IgnoreQueryFilters().Where(s => s.PoleId == pole).Select(s => s.FarFromPole).SingleAsync()));
    }

    [Fact]
    public async Task Bad_fields_are_400_naming_the_field()
    {
        var (_, code) = await PoleWithCodeAsync();
        var anonymous = host.CreateClient();

        foreach (var (response, field) in new[]
        {
            (await Submit(anonymous, code, "on_fire"), "kind"),
            (await Submit(anonymous, code, "lamp_out", lat: 11.3), "lat"),
            (await Submit(anonymous, code, "lamp_out", description: new string('x', 1001)), "description"),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            // Enum values follow the repo-wide WireEnum shape ({<name>: value, allowed: […]}); the others name a "field".
            var details = (await Read(response)).GetProperty("error").GetProperty("details");
            Assert.True(details.TryGetProperty(field, out _)
                || (details.TryGetProperty("field", out var named) && named.GetString() == field), details.GetRawText());
        }
    }

    /// <summary>
    /// The limiter partitions by client address. From an UNTRUSTED peer, <c>X-Forwarded-For</c> is ignored, so changing it does not
    /// buy a fresh bucket; from a configured proxy it is honoured, so two citizens behind it do not share one (D-8).
    /// </summary>
    [Fact]
    public async Task Forwarded_addresses_count_only_from_a_trusted_proxy()
    {
        await using var limited = Host(("RateLimits:PublicLookup:PermitLimit", "2"), ("ForwardedHeaders:KnownProxies:0", "10.0.0.1"));

        var untrusted = await LookupsFrom(limited, peer: "10.0.0.9", forwarded: ["1.1.1.1", "2.2.2.2", "3.3.3.3"]);
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], untrusted);

        var trusted = await LookupsFrom(limited, peer: "10.0.0.1", forwarded: ["4.4.4.4", "5.5.5.5", "6.6.6.6"]);
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], trusted);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private WebApplicationFactory<Program> Host(params (string Key, string Value)[] settings)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:PublicLookup:PermitLimit", "10000");
            builder.UseSetting("RateLimits:PublicReport:PermitLimit", "10000");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IObjectStore>(photos));
                services.AddSingleton<IStartupFilter, TestPeerAddress>();
            });
        });

    private static async Task<HttpStatusCode[]> LookupsFrom(WebApplicationFactory<Program> limited, string peer, string[] forwarded)
    {
        var client = limited.CreateClient();
        var codes = new List<HttpStatusCode>();
        foreach (var address in forwarded)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Lookup) { Content = JsonContent.Create(new { code = "x" }) };
            request.Headers.Add(TestPeerAddress.Header, peer);
            request.Headers.Add("X-Forwarded-For", address);
            codes.Add((await client.SendAsync(request)).StatusCode);
        }

        return [.. codes];
    }

    private async Task<(string Pole, string Code)> PoleWithCodeAsync()
    {
        var pole = await factory.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var road = new RoadSegment
            {
                CommuneId = factory.CommuneId, SegmentName = "Citizen road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Field,
                Geom = new LineString([new Coordinate(107.5, 11.4), new Coordinate(107.51, 11.41)]) { SRID = 4326 }, LengthM = 100,
            };
            db.Add(road);
            await db.SaveChangesAsync();
            var planted = new Pole { CommuneId = factory.CommuneId, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.505, 11.405) { SRID = 4326 } };
            db.Add(planted);
            await db.SaveChangesAsync();
            return planted.PoleId;
        });
        var issued = await Read(await (await factory.ManagerClientAsync()).PostAsJsonAsync("/api/v1/qr-codes", new { pole_id = pole }));
        return (pole, issued.GetProperty("code").GetString()!);
    }

    private static async Task<HttpResponseMessage> Submit(
        HttpClient client, string code, string kind, Guid? key = null, string? description = null,
        double? lat = null, double? lng = null, byte[][]? photos = null)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(code), "code" },
            { new StringContent(kind), "kind" },
            { new StringContent((key ?? Guid.NewGuid()).ToString()), "client_op_id" },
        };
        if (description is not null) form.Add(new StringContent(description), "description");
        if (lat is { } la) form.Add(new StringContent(la.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lat");
        if (lng is { } ln) form.Add(new StringContent(ln.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lng");
        foreach (var (photo, index) in (photos ?? []).Select((p, i) => (p, i)))
        {
            form.Add(new ByteArrayContent(photo), "photos", $"photo{index}.jpg");
        }

        return await client.PostAsync(Reports, form);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>A JPEG carrying EXIF with a GPS position — what a phone camera writes.</summary>
    private static byte[] Exif()
    {
        using var image = new Image<Rgb24>(320, 240);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "TestPhone");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(11, 1), new Rational(24, 1), new Rational(0, 1)]);
        image.Metadata.ExifProfile = exif;
        using var output = new MemoryStream();
        image.SaveAsJpeg(output);
        var bytes = output.ToArray();
        using var check = Image.Load(bytes);
        Assert.NotNull(check.Metadata.ExifProfile);
        return bytes;
    }

    /// <summary>TestServer has no client address; this sets one from a header, before the forwarded-headers middleware runs.</summary>
    private sealed class TestPeerAddress : IStartupFilter
    {
        public const string Header = "X-Test-Peer";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var peer)) context.Connection.RemoteIpAddress = IPAddress.Parse(peer!);
                await nextMiddleware();
            });
            next(app);
        };
    }
}
