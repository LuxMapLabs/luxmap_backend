using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// POLE-NOTE — one free-text note per pole, written by a field engineer or a manager through its own
/// endpoint, and read in the inventory, the map's pole detail and a work order's pole list.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleNoteTests(AssetImportFixture fixture)
{
    private const double Lng = 107.71;
    private const double Lat = 11.71;

    private static string NoteUrl(string poleId) => $"/api/v1/assets/poles/{poleId}/note";

    [Fact]
    public async Task A_field_engineer_writes_a_note_and_all_three_reads_show_it_with_who_and_when()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var engineer = await fixture.FieldEngineerClientAsync();

        var response = await engineer.PutAsJsonAsync(NoteUrl(poleId), new { note = "  Trước cổng trường tiểu học, giờ tan học đông xe.  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var written = (await Json(response)).GetProperty("note");
        Assert.Equal("Trước cổng trường tiểu học, giờ tan học đông xe.", written.GetProperty("text").GetString());
        Assert.Equal("Kỹ sư hiện trường thử", written.GetProperty("updated_by_name").GetString());
        Assert.StartsWith("USR-", written.GetProperty("updated_by").GetString());

        var manager = await fixture.ManagerClientAsync();
        var inventory = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"))).GetProperty("pole").GetProperty("note");
        var map = (await Json(await manager.GetAsync($"/api/v1/map/poles/{poleId}"))).GetProperty("note");

        foreach (var read in new[] { inventory, map })
        {
            Assert.Equal(written.GetProperty("text").GetString(), read.GetProperty("text").GetString());
            Assert.Equal(written.GetProperty("updated_by").GetString(), read.GetProperty("updated_by").GetString());
            Assert.Equal(written.GetProperty("updated_at").GetString(), read.GetProperty("updated_at").GetString());
        }
    }

    [Fact]
    public async Task A_manager_overwrites_it_and_null_or_blank_clears_it_but_keeps_who_cleared()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var manager = await fixture.ManagerClientAsync();

        await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(NoteUrl(poleId), new { note = "ghi chú đầu" });
        var overwritten = await Json(await manager.PutAsJsonAsync(NoteUrl(poleId), new { note = "ghi chú của quản lý" }));
        Assert.Equal("ghi chú của quản lý", overwritten.GetProperty("note").GetProperty("text").GetString());

        foreach (var clear in new object[] { new { note = (string?)null }, new { note = "   " } })
        {
            var cleared = await Json(await manager.PutAsJsonAsync(NoteUrl(poleId), clear));
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("note").ValueKind);
        }

        var stored = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AsNoTracking().Where(p => p.PoleId == poleId)
            .Select(p => new { p.Note, p.NoteUpdatedBy, p.NoteUpdatedAt }).SingleAsync());
        Assert.Null(stored.Note);
        Assert.NotNull(stored.NoteUpdatedBy);
        Assert.NotNull(stored.NoteUpdatedAt);
    }

    [Fact]
    public async Task A_body_without_the_key_or_with_more_than_1000_characters_is_400()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var engineer = await fixture.FieldEngineerClientAsync();

        var missing = await engineer.PutAsJsonAsync(NoteUrl(poleId), new { });
        var tooLong = await engineer.PutAsJsonAsync(NoteUrl(poleId), new { note = new string('x', 1001) });
        var exactly = await engineer.PutAsJsonAsync(NoteUrl(poleId), new { note = new string('x', 1000) });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await tooLong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, exactly.StatusCode);
    }

    [Fact]
    public async Task A_pole_outside_the_callers_commune_is_404()
    {
        var foreignPole = await NewPoleAsync(fixture.ForeignCommuneId);

        var response = await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(NoteUrl(foreignPole), new { note = "không được" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The full-replacement PUT clears whatever its body leaves out (it is a decision, see
    /// <c>Replacing_a_pole_without_a_feeder_id_clears_its_circuit</c>) — but the note is not one of its
    /// fields, so replacing a pole can never wipe an engineer's note.
    /// </summary>
    [Fact]
    public async Task Replacing_the_pole_keeps_its_note()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var manager = await fixture.ManagerClientAsync();
        await manager.PutAsJsonAsync(NoteUrl(poleId), new { note = "cột nghiêng" });

        var detail = await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"));
        var replace = await manager.PutAsJsonAsync($"/api/v1/assets/poles/{poleId}", new
        {
            segment_id = detail.GetProperty("pole").GetProperty("segment_id").GetString(),
            geom_wkt = detail.GetProperty("geom_wkt").GetString(),
            near_sensitive_poi = true,
            data_source = "public_imagery",
        });
        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);

        var after = await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"));
        Assert.True(after.GetProperty("pole").GetProperty("near_sensitive_poi").GetBoolean());
        Assert.Equal("cột nghiêng", after.GetProperty("pole").GetProperty("note").GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_database_refuses_a_note_longer_than_1000_characters_from_any_writer()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
                pole.Note = new string('x', 1001);
                pole.NoteUpdatedBy = await db.Set<Modules.Identity.Entities.AppUser>().Select(u => u.UserId).FirstAsync();
                pole.NoteUpdatedAt = DateTime.UtcNow;
                return await db.SaveChangesAsync();
            }
        }));

        Assert.Equal("ck_pole_note_length", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }

    private Task<string> NewPoleAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "note probe road", RoadClass = RoadClass.InterVillage, LengthM = 100,
                    Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat + 0.01)]) { SRID = 4326 },
                    CommuneId = communeId, DataSource = DataSource.PublicImagery,
                };
                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();

                var pole = new Pole
                {
                    SegmentId = segment.SegmentId, CommuneId = communeId,
                    Geom = new Point(Lng, Lat) { SRID = 4326 }, DataSource = DataSource.PublicImagery,
                };
                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
