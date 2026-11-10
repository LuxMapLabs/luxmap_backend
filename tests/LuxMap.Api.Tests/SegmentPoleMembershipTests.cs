using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>All rows use the shared fixture's communes; its teardown deletes poles before segments and accounts.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SegmentPoleMembershipTests(AssetImportFixture fixture)
{
    private const string Segments = "/api/v1/assets/segments";
    private const string Poles = "/api/v1/assets/poles";
    private readonly string tag = $"segp-{Guid.NewGuid():N}";

    [Fact]
    public async Task Post_assigns_only_requested_poles_and_absent_or_empty_assigns_none()
    {
        using var client = await fixture.ManagerClientAsync();
        var a = await NewPole(client);
        var b = await NewPole(client);
        var other = await NewPole(client);
        var segment = await NewSegment(client, new[] { b, a });
        Assert.Equal(segment, await SegmentOf(client, a));
        Assert.Equal(segment, await SegmentOf(client, b));
        Assert.Null(await SegmentOf(client, other));
        await NewSegment(client);
        await NewSegment(client, Array.Empty<string>());
        Assert.Equal(segment, await SegmentOf(client, a));
        Assert.Equal(segment, await SegmentOf(client, b));
        Assert.Null(await SegmentOf(client, other));
    }

    [Fact]
    public async Task Put_replaces_visible_membership_but_keeps_unchanged_poles_and_absent_membership()
    {
        using var client = await fixture.ManagerClientAsync();
        var a = await NewPole(client);
        var b = await NewPole(client);
        var c = await NewPole(client);
        var segment = await NewSegment(client, new[] { a, b });
        var beforeA = await Stamp(client, a);
        var beforeB = await Stamp(client, b);
        var beforeC = await Stamp(client, c);
        await Put(client, segment, new[] { b, c });
        Assert.Null(await SegmentOf(client, a));
        Assert.Equal(segment, await SegmentOf(client, b));
        Assert.Equal(segment, await SegmentOf(client, c));
        Assert.True(await Stamp(client, a) > beforeA);
        Assert.Equal(beforeB, await Stamp(client, b));
        Assert.True(await Stamp(client, c) > beforeC);
        await Put(client, segment);
        Assert.Null(await SegmentOf(client, a));
        Assert.Equal(segment, await SegmentOf(client, b));
        Assert.Equal(segment, await SegmentOf(client, c));
        await Put(client, segment, Array.Empty<string>());
        Assert.Null(await SegmentOf(client, a));
        Assert.Null(await SegmentOf(client, b));
        Assert.Null(await SegmentOf(client, c));
    }

    public static IEnumerable<object[]> InvalidSets()
    {
        foreach (var json in new[] { "null", "{}", "\"text\"", "[1]", "[null]", "[\"\"]", "[\"POLE-123\"]",
                     "[\"POLE-١٢٣٤\"]", "[\"POLE-1234\\n\"]", "[\"POLE-1234\",\"POLE-1234\"]" })
            yield return [json];
        yield return [JsonSerializer.Serialize(Enumerable.Range(1, 2001).Select(i => $"POLE-{i:D4}"))];
    }

    [Theory]
    [MemberData(nameof(InvalidSets))]
    public async Task Invalid_sets_are_400_before_any_lookup_on_post_and_put(string json)
    {
        using var client = await fixture.ManagerClientAsync();
        var body = Body();
        body["pole_ids"] = JsonDocument.Parse(json).RootElement.Clone();
        // An unknown commune would otherwise fail the first POST lookup; an unknown segment fails PUT.
        body["commune_id"] = "COM-segp-missing";
        await Error(await client.PostAsJsonAsync(Segments, body), HttpStatusCode.BadRequest, "VALIDATION_FAILED", "pole_ids");
        body.Remove("commune_id");
        await Error(await client.PutAsJsonAsync($"{Segments}/SEG-segp-missing", body), HttpStatusCode.BadRequest,
            "VALIDATION_FAILED", "pole_ids");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_hidden_pole_is_404_and_neither_segment_nor_valid_pole_changes(bool hidden)
    {
        using var client = await fixture.ManagerClientAsync();
        using var foreign = await fixture.ForeignCommuneClientAsync();
        var valid = await NewPole(client);
        var missing = hidden ? await NewPole(foreign, fixture.ForeignCommuneId) : "POLE-999999999999999999";
        var segment = await NewSegment(client);
        var before = await SegmentRow(client, segment);
        var stamp = await Stamp(client, valid);
        foreach (var post in new[] { true, false })
        {
            var body = Body(new[] { valid, missing });
            body["segment_name"] = tag + "-must-not-save";
            var response = post ? await client.PostAsJsonAsync(Segments, body)
                : await client.PutAsJsonAsync($"{Segments}/{segment}", WithoutCommune(body));
            var error = await Error(response, HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
            Assert.Equal(missing, error.GetProperty("details").GetProperty("pole_id").GetString());
            Assert.Null(await SegmentOf(client, valid));
            Assert.Equal(stamp, await Stamp(client, valid));
            Assert.Equal(before, await SegmentRow(client, segment));
            if (post) await AssertNoSegmentNamed(client, tag + "-must-not-save");
        }
    }

    [Fact]
    public async Task Occupied_pole_is_409_with_details_and_every_write_is_atomic()
    {
        using var client = await fixture.ManagerClientAsync();
        var occupied = await NewPole(client);
        var owner = await NewSegment(client, new[] { occupied });
        var free = await NewPole(client);
        var retained = await NewPole(client);
        var target = await NewSegment(client, new[] { retained });
        var before = await SegmentRow(client, target);
        foreach (var post in new[] { true, false })
        {
            var body = Body(new[] { free, occupied });
            body["segment_name"] = tag + "-conflict";
            var response = post ? await client.PostAsJsonAsync(Segments, body)
                : await client.PutAsJsonAsync($"{Segments}/{target}", WithoutCommune(body));
            var error = await Error(response, HttpStatusCode.Conflict, "ASSET_IN_USE");
            Assert.Equal(occupied, error.GetProperty("details").GetProperty("pole_id").GetString());
            Assert.Equal(owner, error.GetProperty("details").GetProperty("segment_id").GetString());
            Assert.Null(await SegmentOf(client, free));
            Assert.Equal(target, await SegmentOf(client, retained));
            Assert.Equal(owner, await SegmentOf(client, occupied));
            Assert.Equal(before, await SegmentRow(client, target));
            if (post) await AssertNoSegmentNamed(client, tag + "-conflict");
        }
    }

    [Fact]
    public async Task Cross_commune_requires_inter_commune_only_for_new_assignments_and_hidden_removals_are_kept()
    {
        using var local = await fixture.ManagerClientAsync();
        using var foreign = await fixture.ForeignCommuneClientAsync();
        using var both = await fixture.LegacyTwoCommuneClientAsync();
        foreach (var post in new[] { true, false })
        {
            var pole = await NewPole(foreign, fixture.ForeignCommuneId);
            var localPole = await NewPole(local);
            var target = await NewSegment(local);
            var body = Body(new[] { localPole, pole });
            var response = post ? await both.PostAsJsonAsync(Segments, body)
                : await both.PutAsJsonAsync($"{Segments}/{target}", WithoutCommune(body));
            var error = await Error(response, HttpStatusCode.Conflict, "CROSS_COMMUNE_REFERENCE");
            Assert.Equal(fixture.ForeignCommuneId, error.GetProperty("details").GetProperty("pole_commune_id").GetString());
            Assert.Equal(fixture.CommuneId, error.GetProperty("details").GetProperty("segment_commune_id").GetString());
            Assert.Null(await SegmentOf(foreign, pole));
            Assert.Null(await SegmentOf(local, localPole));
            if (post) target = await NewSegment(both, new[] { pole }, "inter_commune");
            else await Put(both, target, new[] { pole }, "inter_commune");
            Assert.Equal(target, await SegmentOf(foreign, pole));
            var stamp = await Stamp(foreign, pole);
            // Existing membership survives changing road_class in the same PUT.
            await Put(both, target, new[] { pole }, "inter_village");
            Assert.Equal(stamp, await Stamp(foreign, pole));
            await Put(local, target, Array.Empty<string>());
            Assert.Equal(target, await SegmentOf(foreign, pole));
            Assert.Equal(stamp, await Stamp(foreign, pole));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_assignments_have_one_winner_and_one_conflict(bool post)
    {
        using var client = await fixture.ManagerClientAsync();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var pole = await NewPole(client);
            var targets = new[] { await NewSegment(client), await NewSegment(client) };
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
            await using var held = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM pole WHERE pole_id = {0} FOR NO KEY UPDATE", pole);
            Task<HttpResponseMessage> Send(int index) => post
                ? client.PostAsJsonAsync(Segments, Body(new[] { pole }))
                : client.PutAsJsonAsync($"{Segments}/{targets[index]}", WithoutCommune(Body(new[] { pole })));
            var first = Send(0);
            var second = Send(1);
            try
            {
                // Wait for BOTH requests to reach the database lock, not an arbitrary sleep. Without FOR UPDATE,
                // neither request waits on that SELECT, so the bounded wait fails before releasing the holder.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (true)
                {
                    var waiting = await fixture.QueryAsync(monitor => monitor.Database.SqlQueryRaw<int>("""
                        SELECT count(*)::int AS "Value" FROM pg_stat_activity
                        WHERE datname = current_database() AND wait_event_type = 'Lock'
                        AND query LIKE 'SELECT 1 FROM pole WHERE pole_id = ANY%FOR UPDATE%'
                        """).SingleAsync(deadline.Token));
                    if (waiting == 2) break;
                    Assert.False(first.IsCompleted || second.IsCompleted, "Both requests must reach the pole lock before either writes.");
                    await Task.Yield();
                }
            }
            finally
            {
                await held.RollbackAsync();
            }
            var responses = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
            var success = post ? HttpStatusCode.Created : HttpStatusCode.NoContent;
            Assert.Single(responses, response => response.StatusCode == success);
            await Error(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict, "ASSET_IN_USE");
            var winner = Array.FindIndex(responses, response => response.StatusCode == success);
            var winnerId = post ? Id(responses[winner]) : targets[winner];
            Assert.Equal(winnerId, await SegmentOf(client, pole));
        }
    }

    // The fixture pole sits on the first vertex of Body()'s line; Far is about 11 km east of it.
    private const string Far = "LINESTRING(106.60 10.97,106.61 10.98)";

    [Fact]
    public async Task Put_rerouting_a_segment_away_from_its_poles_is_409_and_writes_nothing()
    {
        using var client = await fixture.ManagerClientAsync();
        var kept = await NewPole(client);
        var dropped = await NewPole(client);
        var segment = await NewSegment(client, new[] { kept, dropped });
        var before = await SegmentRow(client, segment);

        var body = WithoutCommune(Body(new[] { kept }));
        body["geom_wkt"] = Far;
        body["segment_name"] = tag + "-must-not-save";
        var error = await Error(await client.PutAsJsonAsync($"{Segments}/{segment}", body),
            HttpStatusCode.Conflict, "POLE_OFF_SEGMENT");
        var details = error.GetProperty("details");
        Assert.Equal(segment, details.GetProperty("segment_id").GetString());
        // Only poles that stay on the segment are measured: the pole this very PUT detaches is not named.
        Assert.Equal([kept], details.GetProperty("pole_ids").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(1, details.GetProperty("pole_count").GetInt32());
        Assert.Equal(25, details.GetProperty("corridor_m").GetDouble());
        Assert.Equal(before, await SegmentRow(client, segment));
        Assert.Equal(segment, await SegmentOf(client, dropped));

        // A new line that still passes the poles is accepted, and so is any line once no pole is left on it.
        body["geom_wkt"] = "LINESTRING(106.49 10.97,106.52 10.99)";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Segments}/{segment}", body)).StatusCode);
        body["geom_wkt"] = Far;
        body["pole_ids"] = Array.Empty<string>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Segments}/{segment}", body)).StatusCode);
    }

    [Fact]
    public async Task Put_rerouting_counts_a_hidden_pole_without_naming_it()
    {
        using var local = await fixture.ManagerClientAsync();
        using var foreign = await fixture.ForeignCommuneClientAsync();
        using var both = await fixture.LegacyTwoCommuneClientAsync();
        var hidden = await NewPole(foreign, fixture.ForeignCommuneId);
        var visible = await NewPole(local);
        var segment = await NewSegment(both, new[] { hidden, visible }, "inter_commune");
        var before = await SegmentRow(local, segment);

        // What a form that rebuilds the line from the poles it can see would send.
        var body = WithoutCommune(Body(Array.Empty<string>(), "inter_commune"));
        body["geom_wkt"] = Far;
        var response = await local.PutAsJsonAsync($"{Segments}/{segment}", body);
        var text = await response.Content.ReadAsStringAsync();
        var error = await Error(response, HttpStatusCode.Conflict, "POLE_OFF_SEGMENT");
        Assert.Empty(error.GetProperty("details").GetProperty("pole_ids").EnumerateArray());
        Assert.Equal(1, error.GetProperty("details").GetProperty("pole_count").GetInt32());
        Assert.DoesNotContain(hidden, text);
        Assert.Equal(before, await SegmentRow(local, segment));
        Assert.Equal(segment, await SegmentOf(local, visible));
        Assert.Equal(segment, await SegmentOf(foreign, hidden));
    }

    [Fact]
    public async Task Put_keeping_the_geometry_never_measures_the_corridor()
    {
        using var client = await fixture.ManagerClientAsync();
        var pole = await NewPole(client);
        var created = await client.PostAsJsonAsync(Segments, new Dictionary<string, object?>(Body(new[] { pole })) { ["geom_wkt"] = Far });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var segment = Id(created);

        // The pole is already 11 km off: a rename with the same line must not be held hostage by old data.
        var body = WithoutCommune(Body());
        body["geom_wkt"] = Far;
        body["segment_name"] = tag + "-renamed";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{Segments}/{segment}", body)).StatusCode);
        Assert.Contains(tag + "-renamed", await SegmentRow(client, segment));
    }

    private Dictionary<string, object?> Body(object? ids = null, string roadClass = "inter_village")
    {
        var body = new Dictionary<string, object?>
        {
            ["segment_name"] = tag, ["road_class"] = roadClass, ["length_m"] = 100,
            ["geom_wkt"] = "LINESTRING(106.49 10.97,106.50 10.98)",
            ["commune_id"] = fixture.CommuneId, ["data_source"] = "public_imagery",
        };
        if (ids is not null) body["pole_ids"] = ids;
        return body;
    }

    private static Dictionary<string, object?> WithoutCommune(Dictionary<string, object?> body)
    {
        body.Remove("commune_id");
        return body;
    }

    private async Task<string> NewSegment(HttpClient client, object? ids = null, string roadClass = "inter_village")
    {
        var response = await client.PostAsJsonAsync(Segments, Body(ids, roadClass));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return Id(response);
    }

    private async Task Put(HttpClient client, string segment, object? ids = null, string roadClass = "inter_village")
    {
        var response = await client.PutAsJsonAsync($"{Segments}/{segment}", WithoutCommune(Body(ids, roadClass)));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> NewPole(HttpClient client, string? commune = null)
    {
        var response = await client.PostAsJsonAsync(Poles, new
        {
            external_ref = $"{tag}-{Guid.NewGuid():N}"[..60], commune_id = commune ?? fixture.CommuneId,
            geom_wkt = "POINT(106.49 10.97)", data_source = "public_imagery",
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return Id(response);
    }

    private static string Id(HttpResponseMessage response) => response.Headers.Location!.ToString().Split('/').Last();

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> SegmentOf(HttpClient client, string pole)
        => (await Get(client, $"{Poles}/{pole}")).GetProperty("pole").GetProperty("segment_id").GetString();

    private static async Task<DateTime> Stamp(HttpClient client, string pole)
        => (await Get(client, $"{Poles}/{pole}")).GetProperty("pole").GetProperty("updated_at").GetDateTime();

    private static async Task<string> SegmentRow(HttpClient client, string segment)
        => (await Get(client, $"{Segments}/{segment}")).GetRawText();

    private async Task AssertNoSegmentNamed(HttpClient client, string name)
    {
        for (var number = 1; ; number++)
        {
            var page = await Get(client, $"{Segments}?commune_id={fixture.CommuneId}&page_size=200&page={number}");
            Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("segment_name").GetString() == name);
            if (number * 200 >= page.GetProperty("total").GetInt32()) break;
        }
    }

    private static async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, text);
        var error = JsonDocument.Parse(text).RootElement.GetProperty("error").Clone();
        Assert.Equal(code, error.GetProperty("code").GetString());
        if (field is not null) Assert.Equal(field, error.GetProperty("details").GetProperty("field").GetString());
        return error;
    }
}
