using System.Globalization;
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
    /// The full-replacement PUT clears whatever its body leaves out (a decision, see
    /// <c>Replacing_a_pole_without_a_feeder_id_clears_its_circuit</c>) — except the note: a form that
    /// predates it must not wipe what an engineer wrote, nor make the manager its author.
    /// </summary>
    [Fact]
    public async Task Replacing_the_pole_without_the_note_key_keeps_the_note_and_its_author()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var written = await Json(await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(NoteUrl(poleId), new { note = "cột nghiêng" }));
        var manager = await fixture.ManagerClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await ReplaceAsync(manager, poleId, new { near_sensitive_poi = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ReplaceAsync(manager, poleId, new { near_sensitive_poi = true, note = "cột nghiêng" })).StatusCode);

        var after = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"))).GetProperty("pole");
        Assert.True(after.GetProperty("near_sensitive_poi").GetBoolean());
        Assert.Equal("cột nghiêng", after.GetProperty("note").GetProperty("text").GetString());
        Assert.Equal(written.GetProperty("note").GetProperty("updated_by").GetString(), after.GetProperty("note").GetProperty("updated_by").GetString());
        Assert.Equal(written.GetProperty("note").GetProperty("updated_at").GetString(), after.GetProperty("note").GetProperty("updated_at").GetString());
    }

    [Fact]
    public async Task The_edit_form_overwrites_or_clears_the_note_and_the_manager_becomes_its_author()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId);
        await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(NoteUrl(poleId), new { note = "ghi chú của kỹ sư" });
        var manager = await fixture.ManagerClientAsync();

        await ReplaceAsync(manager, poleId, new { note = "  quản lý sửa  " });
        var edited = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"))).GetProperty("pole").GetProperty("note");
        Assert.Equal("quản lý sửa", edited.GetProperty("text").GetString());
        Assert.Equal("BE-12a commune-scoped manager", edited.GetProperty("updated_by_name").GetString());

        await ReplaceAsync(manager, poleId, new { note = (string?)null });
        var cleared = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"))).GetProperty("pole");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("note").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReplaceAsync(manager, poleId, new { note = new string('x', 1001) })).StatusCode);
    }

    [Fact]
    public async Task The_create_form_takes_an_optional_note()
    {
        var existing = await NewPoleAsync(fixture.CommuneId);
        var manager = await fixture.ManagerClientAsync();
        var segmentId = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{existing}"))).GetProperty("pole").GetProperty("segment_id").GetString();
        object Body(object? note) => new
        {
            segment_id = segmentId, commune_id = fixture.CommuneId, geom_wkt = $"POINT ({Lng} {Lat})", data_source = "public_imagery", note,
        };

        var created = await manager.PostAsJsonAsync("/api/v1/assets/poles", Body("Gần chợ"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = (await Json(await manager.GetAsync(created.Headers.Location!))).GetProperty("pole").GetProperty("note");
        Assert.Equal("Gần chợ", detail.GetProperty("text").GetString());

        var withoutNote = await manager.PostAsJsonAsync("/api/v1/assets/poles", Body(null));
        Assert.Equal(JsonValueKind.Null, (await Json(await manager.GetAsync(withoutNote.Headers.Location!))).GetProperty("pole").GetProperty("note").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/assets/poles", Body(new string('x', 1001)))).StatusCode);
    }

    [Fact]
    public async Task The_import_fills_a_note_on_a_new_pole_and_on_a_pole_that_never_had_one()
    {
        var manager = await fixture.ManagerClientAsync();
        var tag = await ImportSegmentAsync(manager);

        var created = await ImportPolesAsync(manager, tag, ("P1", "\"Gần trường TH Long Phước, tan học 17h\""), ("P2", ""));
        Assert.Equal(2, created.GetProperty("inserted").GetInt32());
        Assert.Equal(0, created.GetProperty("total_warnings").GetInt32());

        var filled = await ImportPolesAsync(manager, tag, ("P1", "\"Gần trường TH Long Phước, tan học 17h\""), ("P2", "Gần chợ"));
        Assert.Equal(2, filled.GetProperty("updated").GetInt32());
        Assert.Equal(0, filled.GetProperty("total_warnings").GetInt32());

        var p1 = await StoredNoteAsync(tag, "P1");
        var p2 = await StoredNoteAsync(tag, "P2");
        Assert.Equal("Gần trường TH Long Phước, tan học 17h", p1.Note);
        Assert.Equal("Gần chợ", p2.Note);
        Assert.Equal(p1.NoteUpdatedBy, p2.NoteUpdatedBy);
        Assert.Equal("BE-12a commune-scoped manager", await fixture.QueryAsync(db =>
            db.Set<Modules.Identity.Entities.AppUser>().Where(u => u.UserId == p1.NoteUpdatedBy).Select(u => u.FullName).SingleAsync()));
    }

    /// <summary>
    /// An engineer may have changed the note on site since the inventory file was written: re-importing that
    /// file must neither put the old text back nor resurrect a note someone cleared. The row's other fields
    /// still apply, and the skipped note comes back as a warning rather than an error.
    /// </summary>
    [Fact]
    public async Task The_import_never_overwrites_or_restores_a_note_and_warns_instead()
    {
        var manager = await fixture.ManagerClientAsync();
        var engineer = await fixture.FieldEngineerClientAsync();
        var tag = await ImportSegmentAsync(manager);
        await ImportPolesAsync(manager, tag, ("P1", "ghi chú trong file"));
        var poleId = (await StoredNoteAsync(tag, "P1")).PoleId;

        await engineer.PutAsJsonAsync(NoteUrl(poleId), new { note = "kỹ sư sửa ngoài hiện trường" });
        var edited = await StoredNoteAsync(tag, "P1");

        var overwrite = await ImportPolesAsync(manager, tag, ("P1", "ghi chú trong file"));
        Assert.Equal(1, overwrite.GetProperty("updated").GetInt32());
        Assert.Equal(0, overwrite.GetProperty("failed").GetInt32());
        var warning = Assert.Single(overwrite.GetProperty("warnings").EnumerateArray());
        Assert.Equal("note", warning.GetProperty("column").GetString());
        Assert.Equal(2, warning.GetProperty("row").GetInt32());
        Assert.Equal(edited, await StoredNoteAsync(tag, "P1"));

        foreach (var unchanged in new[] { "", "kỹ sư sửa ngoài hiện trường" })
        {
            Assert.Equal(0, (await ImportPolesAsync(manager, tag, ("P1", unchanged))).GetProperty("total_warnings").GetInt32());
            Assert.Equal(edited, await StoredNoteAsync(tag, "P1"));
        }

        await engineer.PutAsJsonAsync(NoteUrl(poleId), new { note = (string?)null });
        var cleared = await StoredNoteAsync(tag, "P1");

        var restore = await ImportPolesAsync(manager, tag, ("P1", "ghi chú trong file"));
        Assert.Equal(1, restore.GetProperty("total_warnings").GetInt32());
        Assert.Null((await StoredNoteAsync(tag, "P1")).Note);
        Assert.Equal(cleared, await StoredNoteAsync(tag, "P1"));
    }

    [Fact]
    public async Task An_imported_note_longer_than_1000_characters_fails_its_row()
    {
        var manager = await fixture.ManagerClientAsync();
        var tag = await ImportSegmentAsync(manager);

        var result = await ImportPolesAsync(manager, tag, ("P1", new string('x', 1001)), ("P2", new string('x', 1000)));

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var error = Assert.Single(result.GetProperty("rows").EnumerateArray());
        Assert.Equal("note", error.GetProperty("column").GetString());
        Assert.Equal(2, error.GetProperty("row").GetInt32());
    }

    private async Task<string> ImportSegmentAsync(HttpClient manager)
    {
        var tag = $"N{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var result = await AssetImportTests.ImportAsync(manager, "segments", "segments.csv",
            "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source\n"
            + string.Create(CultureInfo.InvariantCulture,
                $"{tag}-S1,note import road,inter_village,100,\"LINESTRING({Lng} {Lat}, {Lng + 0.01} {Lat + 0.01})\",{fixture.CommuneId},public_imagery"));
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        return tag;
    }

    /// <summary>A poles file with a <c>note</c> column; each row is <c>(suffix, raw note cell)</c>.</summary>
    private Task<JsonElement> ImportPolesAsync(HttpClient manager, string tag, params (string Suffix, string Note)[] rows)
        => AssetImportTests.ImportAsync(manager, "poles", "poles.csv",
            "external_ref,segment_external_ref,commune_id,geom_wkt,data_source,note"
            + string.Concat(rows.Select(row => string.Create(CultureInfo.InvariantCulture,
                $"\n{tag}-{row.Suffix},{tag}-S1,{fixture.CommuneId},POINT({Lng} {Lat}),public_imagery,{row.Note}"))));

    private Task<StoredNote> StoredNoteAsync(string tag, string suffix)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.ExternalRef == $"{tag}-{suffix}")
            .Select(p => new StoredNote(p.PoleId, p.Note, p.NoteUpdatedBy, p.NoteUpdatedAt)).SingleAsync());

    private sealed record StoredNote(string PoleId, string? Note, string? NoteUpdatedBy, DateTime? NoteUpdatedAt);

    /// <summary>The full-replacement PUT with the pole's current values, plus whatever <paramref name="change"/> overrides.</summary>
    private async Task<HttpResponseMessage> ReplaceAsync(HttpClient manager, string poleId, object change)
    {
        var detail = await Json(await manager.GetAsync($"/api/v1/assets/poles/{poleId}"));
        var body = new Dictionary<string, object?>
        {
            ["segment_id"] = detail.GetProperty("pole").GetProperty("segment_id").GetString(),
            ["geom_wkt"] = detail.GetProperty("geom_wkt").GetString(),
            ["data_source"] = "public_imagery",
        };
        foreach (var property in change.GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(change);
        }

        return await manager.PutAsJsonAsync($"/api/v1/assets/poles/{poleId}", body);
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
