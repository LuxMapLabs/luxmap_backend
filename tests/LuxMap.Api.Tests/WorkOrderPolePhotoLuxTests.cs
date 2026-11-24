using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Api.Tests;

/// <summary>LUX-PHOTO — a lux reading names the pole photo it was taken with; the photo shows the reading back.</summary>
public partial class WorkOrderTests
{
    private const string LuxRoute = "/api/v1/lux-readings";
    private const string LuxMeasuredAt = "2026-10-05T14:01:00Z";

    private static object LuxBody(string pole, string? photo, string? op = null) => new Dictionary<string, object?>
    {
        ["client_op_id"] = op ?? Guid.NewGuid().ToString(), ["pole_id"] = pole, ["photo_id"] = photo,
        ["measured_at"] = LuxMeasuredAt, ["lux_value"] = 7.5, ["meter_model"] = "LuxMap_ESP32", ["data_source"] = "field",
    };

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostLux(object body, string who = "a")
    {
        using var response = await (await PoleClient(who)).PostAsJsonAsync(LuxRoute, body);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
    }

    private async Task<JsonElement> PhotoDetail(string id, string photo)
        => await (await PoleClient("manager")).GetFromJsonAsync<JsonElement>(PhotosPath(id) + "/" + photo);

    [Fact]
    public async Task A_reading_linked_to_a_photo_is_echoed_and_shown_on_the_photo()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);

        var before = await PhotoDetail(id, photo);
        foreach (var field in new[] { "lux_id", "lux_value", "lux_measured_at" })
            Assert.Equal(JsonValueKind.Null, before.GetProperty(field).ValueKind);

        var (status, reading) = await PostLux(LuxBody(pole, photo));
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(photo, reading.GetProperty("photo_id").GetString());
        var luxId = reading.GetProperty("lux_id").GetString();

        var client = await PoleClient("manager");
        var series = await client.GetFromJsonAsync<JsonElement>($"{LuxRoute}/poles/{pole}");
        Assert.Equal(photo, Assert.Single(series.GetProperty("items").EnumerateArray()).GetProperty("photo_id").GetString());
        var bulk = await client.GetFromJsonAsync<JsonElement>($"{LuxRoute}?pole_id={pole}");
        Assert.Equal(photo, Assert.Single(bulk.GetProperty("items").EnumerateArray()).GetProperty("photo_id").GetString());

        var after = await PhotoDetail(id, photo);
        Assert.Equal(luxId, after.GetProperty("lux_id").GetString());
        Assert.Equal(7.5, after.GetProperty("lux_value").GetDouble());
        Assert.Equal(DateTime.Parse(LuxMeasuredAt).ToUniversalTime(), after.GetProperty("lux_measured_at").GetDateTime().ToUniversalTime());

        // The list reads all linked readings in one batch; an unlinked photo next to it stays null.
        var other = await NewPolePhoto(id, pole, "2026-10-05T14:05:00Z");
        var list = await client.GetFromJsonAsync<JsonElement>(PhotosPath(id) + "?pole_id=" + pole);
        var items = list.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("photo_id").GetString()!);
        Assert.Equal(luxId, items[photo].GetProperty("lux_id").GetString());
        Assert.Equal(JsonValueKind.Null, items[other].GetProperty("lux_id").ValueKind);
    }

    [Fact]
    public async Task A_reading_without_a_photo_behaves_as_before()
    {
        var pole = await PlantPole(.2);
        var body = new Dictionary<string, object?>
        {
            ["client_op_id"] = Guid.NewGuid().ToString(), ["pole_id"] = pole, ["measured_at"] = LuxMeasuredAt,
            ["lux_value"] = 3.0, ["data_source"] = "field",
        };

        var (status, reading) = await PostLux(body);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(JsonValueKind.Null, reading.GetProperty("photo_id").ValueKind);
    }

    [Fact]
    public async Task An_unknown_or_withdrawn_photo_is_404()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);

        var (unknown, unknownBody) = await PostLux(LuxBody(pole, "PPH-999999999"));
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(ErrorCodes.PolePhotoNotFound, ErrorCode(unknownBody));

        var photo = await NewPolePhoto(id, pole);
        await PoleWithdraw(id, photo);
        var (withdrawn, withdrawnBody) = await PostLux(LuxBody(pole, photo));
        Assert.Equal(HttpStatusCode.NotFound, withdrawn);
        Assert.Equal(ErrorCodes.PolePhotoNotFound, ErrorCode(withdrawnBody));
    }

    [Fact]
    public async Task A_photo_outside_the_callers_communes_is_404_not_a_pole_mismatch()
    {
        // A 400 here would confirm that the photo exists somewhere the caller cannot see.
        var pole = await PlantPole(.2);
        var foreignPole = await PlantPole(.3, commune: foreign);
        var foreignOrder = await Plant(WorkOrderStatus.InProgress, users["b"].UserId, foreign);
        var hidden = await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = new WorkOrderPolePhoto
            {
                WorkOrderId = foreignOrder, PoleId = foreignPole, CommuneId = foreign, DataSource = DataSource.Simulated,
                UploadedBy = users["b"].UserId, UploadedAt = DateTime.UtcNow, CapturedAt = DateTime.UtcNow, Lat = 16, Lng = 108,
                ClientOpId = Guid.NewGuid(), RequestHash = new('a', 64), ObjectKey = "lux-photo", ThumbnailKey = "lux-thumb",
                ByteCount = 4, ThumbnailBytes = 4, Width = 1, Height = 1,
            };
            db.Add(row); await db.SaveChangesAsync();
            return row.PhotoId;
        });

        var (status, body) = await PostLux(LuxBody(pole, hidden));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(ErrorCodes.PolePhotoNotFound, ErrorCode(body));
    }

    [Fact]
    public async Task A_photo_of_another_pole_is_400()
    {
        var pole = await PlantPole(.2); var neighbour = await PlantPole(.4); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, neighbour);

        var (status, body) = await PostLux(LuxBody(pole, photo));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ErrorCodes.PhotoPoleMismatch, ErrorCode(body));
    }

    [Fact]
    public async Task A_photo_takes_one_reading_and_a_retry_of_that_reading_is_still_200()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        var op = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.Created, (await PostLux(LuxBody(pole, photo, op))).Status);
        var (retry, retried) = await PostLux(LuxBody(pole, photo, op));
        Assert.Equal(HttpStatusCode.OK, retry);
        Assert.Equal(photo, retried.GetProperty("photo_id").GetString());

        var (second, secondBody) = await PostLux(LuxBody(pole, photo));
        Assert.Equal(HttpStatusCode.Conflict, second);
        Assert.Equal(ErrorCodes.PhotoLuxAlreadyLinked, ErrorCode(secondBody));
    }

    [Fact]
    public async Task Two_readings_racing_for_one_photo_yield_one_link_and_one_409()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostLux(LuxBody(pole, photo))));

        Assert.Single(results, r => r.Status == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal(ErrorCodes.PhotoLuxAlreadyLinked, ErrorCode(r.Body));
        });
    }

    [Fact]
    public async Task Sync_push_checks_the_photo_the_same_way()
    {
        var pole = await PlantPole(.2); var neighbour = await PlantPole(.4); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, neighbour);
        var op = new Dictionary<string, object?>
        {
            ["client_op_id"] = Guid.NewGuid(), ["op_type"] = "lux_reading",
            ["payload"] = new { pole_id = pole, photo_id = photo, measured_at = LuxMeasuredAt, lux_value = 2.0, data_source = "field" },
        };

        using var response = await (await PoleClient()).PostAsJsonAsync("/api/v1/sync/push", new { operations = new[] { op } });
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ErrorCodes.PhotoPoleMismatch,
            Assert.Single(result.GetProperty("rejected").EnumerateArray()).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Withdrawing_a_photo_keeps_its_reading_and_the_link()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        Assert.Equal(HttpStatusCode.Created, (await PostLux(LuxBody(pole, photo))).Status);

        await PoleWithdraw(id, photo);

        var series = await (await PoleClient("manager")).GetFromJsonAsync<JsonElement>($"{LuxRoute}/poles/{pole}");
        Assert.Equal(photo, Assert.Single(series.GetProperty("items").EnumerateArray()).GetProperty("photo_id").GetString());
    }
}
