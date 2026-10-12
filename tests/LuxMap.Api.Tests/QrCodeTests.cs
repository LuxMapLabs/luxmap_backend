using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// CITIZEN-QR P2a — the Manager issues, revokes and lists the QR codes on poles (CQ-1, CQ-10, D-3, D-16).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class QrCodeTests(AssetImportFixture factory)
{
    private const string Codes = "/api/v1/qr-codes";

    [Fact]
    public async Task Issuing_returns_a_random_code_and_the_public_url_the_web_draws()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var manager = await factory.ManagerClientAsync();

        var response = await manager.PostAsJsonAsync(Codes, new { pole_id = pole });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await Read(response);
        var code = item.GetProperty("code").GetString()!;
        Assert.Matches("^[0-9A-Za-z]{16}$", code);
        Assert.Matches("^QR-[0-9]{4,}$", item.GetProperty("qr_id").GetString()!);
        Assert.EndsWith("/r/" + code, item.GetProperty("url").GetString()!, StringComparison.Ordinal);
        Assert.StartsWith("http", item.GetProperty("url").GetString()!, StringComparison.Ordinal);
        Assert.Equal(pole, item.GetProperty("pole_id").GetString());
        Assert.Equal(factory.CommuneId, item.GetProperty("commune_id").GetString());
        Assert.True(item.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("revoked_at").ValueKind);
    }

    [Fact]
    public async Task Issuing_again_revokes_the_code_in_use_and_the_audit_never_holds_a_code()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var manager = await factory.ManagerClientAsync();

        var first = await Read(await manager.PostAsJsonAsync(Codes, new { pole_id = pole }));
        var second = await Read(await manager.PostAsJsonAsync(Codes, new { pole_id = pole }));

        var listed = (await Get(manager, $"{Codes}?pole_id={pole}")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([Id(first), Id(second)], listed.Select(Id));
        Assert.False(listed[0].GetProperty("active").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, listed[0].GetProperty("revoked_by").ValueKind);
        Assert.True(listed[1].GetProperty("active").GetBoolean());
        Assert.NotEqual(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());

        var events = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.EntityType == AuditEntityType.QrCode && (e.EntityId == Id(first) || e.EntityId == Id(second)))
            .OrderBy(e => e.AuditId).ToListAsync());
        Assert.Equal(
            [(Id(first), AuditAction.Created), (Id(first), AuditAction.Revoked), (Id(second), AuditAction.Created)],
            events.Select(e => (e.EntityId, e.Action)));
        foreach (var code in new[] { first, second }.Select(c => c.GetProperty("code").GetString()!))
        {
            Assert.All(events, e => Assert.DoesNotContain(code, $"{e.BeforeState}|{e.AfterState}|{e.Note}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_revoked_code_stays_revoked()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var manager = await factory.ManagerClientAsync();
        var issued = await Read(await manager.PostAsJsonAsync(Codes, new { pole_id = pole }));

        var revoked = await manager.PostAsync($"{Codes}/{Id(issued)}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.False((await Read(revoked)).GetProperty("active").GetBoolean());

        var again = await manager.PostAsync($"{Codes}/{Id(issued)}/revoke", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.QrCodeAlreadyRevoked, await Code(again));

        var active = await Get(manager, $"{Codes}?pole_id={pole}&active=true");
        Assert.Equal(0, active.GetProperty("total").GetInt32());
    }

    /// <summary>Presses at once on one pole are serialised on the pole row: however many land, exactly one code is in use.</summary>
    [Fact]
    public async Task Simultaneous_presses_leave_exactly_one_code_in_use()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var manager = await factory.ManagerClientAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => manager.PostAsJsonAsync(Codes, new { pole_id = pole })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var all = await Get(manager, $"{Codes}?pole_id={pole}");
        Assert.Equal(5, all.GetProperty("total").GetInt32());
        Assert.Equal(1, all.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("active").GetBoolean()));
    }

    /// <summary>
    /// Issue and revoke both serialise on the POLE row (Codex review P2a): a revoke that locked only the code row let an issue
    /// read the code as in use, then overwrite who revoked it. Proven by holding the pole lock and watching each request wait.
    /// </summary>
    [Fact]
    public async Task Issue_and_revoke_both_wait_for_the_pole_lock()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var manager = await factory.ManagerClientAsync();
        var issued = await Read(await manager.PostAsJsonAsync(Codes, new { pole_id = pole }));

        await HoldingThePoleLockAsync(pole, () => manager.PostAsJsonAsync(Codes, new { pole_id = pole }), HttpStatusCode.Created);
        var current = (await Get(manager, $"{Codes}?pole_id={pole}&active=true")).GetProperty("items")[0];
        await HoldingThePoleLockAsync(pole, () => manager.PostAsync($"{Codes}/{Id(current)}/revoke", null), HttpStatusCode.OK);

        var revokedEvents = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .CountAsync(e => e.EntityType == AuditEntityType.QrCode && e.Action == AuditAction.Revoked
                && (e.EntityId == Id(issued) || e.EntityId == Id(current))));
        Assert.Equal(2, revokedEvents);
    }

    /// <summary>
    /// Two codes written in ONE transaction share <c>created_at</c>, so the id decides — by length first: as text
    /// <c>QR-100000</c> sorts before <c>QR-99999</c> (section 0).
    /// </summary>
    [Fact]
    public async Task The_list_breaks_ties_by_id_length_then_id()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var number = Random.Shared.Next(90_000, 100_000);
        string shorter = $"QR-{number}", longer = $"QR-{number + 10_000}";
        await factory.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var manager = await db.Set<LuxMap.Modules.Identity.Entities.AppUser>().IgnoreQueryFilters()
                .Where(u => u.Username == factory.ManagerUsername).Select(u => u.UserId).SingleAsync();
            var now = DateTime.UtcNow;
            db.Add(Planted(longer, pole, factory.CommuneId, manager, now, revoked: true));
            db.Add(Planted(shorter, pole, factory.CommuneId, manager, now, revoked: false));
            await db.SaveChangesAsync();
            return 0;
        });

        var listed = (await Get(await factory.ManagerClientAsync(), $"{Codes}?pole_id={pole}")).GetProperty("items").EnumerateArray();
        Assert.Equal([shorter, longer], listed.Select(Id));
    }

    /// <summary>The constraints that hold even if the service is wrong — none of them shows in a test that only writes valid data.</summary>
    [Fact]
    public async Task The_schema_enforces_one_active_code_per_pole_and_never_cascades()
    {
        var facts = await factory.QueryAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT conname || ':' || contype::text || ':' || confdeltype::text AS "Value" FROM pg_constraint WHERE conrelid = 'qr_code'::regclass
            UNION ALL SELECT indexname || ':' || indexdef FROM pg_indexes WHERE tablename = 'qr_code'
            """).ToListAsync());

        Assert.Contains(facts, f => f.StartsWith("ux_qr_code_pole_id_active:CREATE UNIQUE INDEX", StringComparison.Ordinal)
            && f.EndsWith("WHERE (revoked_at IS NULL)", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.StartsWith("ix_qr_code_pole_id:CREATE INDEX", StringComparison.Ordinal) && !f.Contains("WHERE", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.StartsWith("ux_qr_code_code:CREATE UNIQUE INDEX", StringComparison.Ordinal));
        foreach (var check in new[] { "ck_qr_code_code_format", "ck_qr_code_revoked_together", "ck_qr_code_revoked_after_created" })
            Assert.Contains($"{check}:c: ", facts);
        var foreignKeys = facts.Where(f => f.StartsWith("fk_qr_code_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, foreignKeys.Length);
        Assert.All(foreignKeys, fk => Assert.EndsWith(":f:r", fk));
    }

    [Fact]
    public async Task Another_communes_pole_and_code_do_not_exist_for_the_caller()
    {
        var foreignPole = await PlantPoleAsync(factory.ForeignCommuneId);
        var both = await factory.BothCommunesClientAsync();
        var foreignCode = await Read(await both.PostAsJsonAsync(Codes, new { pole_id = foreignPole }));
        var manager = await factory.ManagerClientAsync();

        var issue = await manager.PostAsJsonAsync(Codes, new { pole_id = foreignPole });
        Assert.Equal(HttpStatusCode.NotFound, issue.StatusCode);
        Assert.Equal(ErrorCodes.PoleNotFound, await Code(issue));

        var revoke = await manager.PostAsync($"{Codes}/{Id(foreignCode)}/revoke", null);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.Equal(ErrorCodes.QrCodeNotFound, await Code(revoke));

        var list = await manager.GetAsync($"{Codes}?commune_id={factory.ForeignCommuneId}");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(ErrorCodes.CommuneForbidden, await Code(list));

        Assert.DoesNotContain(Id(foreignCode), (await Get(manager, Codes)).GetProperty("items").EnumerateArray().Select(Id));
    }

    [Fact]
    public async Task Only_the_manager_handles_codes()
    {
        var pole = await PlantPoleAsync(factory.CommuneId);
        var engineer = await factory.FieldEngineerClientAsync();

        foreach (var response in new[] { await engineer.PostAsJsonAsync(Codes, new { pole_id = pole }), await engineer.GetAsync(Codes) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ErrorCodes.RoleForbidden, await Code(response));
        }
    }

    [Fact]
    public async Task A_missing_pole_id_is_a_400_naming_the_field()
    {
        var response = await (await factory.ManagerClientAsync()).PostAsJsonAsync(Codes, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await Code(response));
    }

    private static string Id(JsonElement item) => item.GetProperty("qr_id").GetString()!;

    private static LuxMap.Modules.Citizen.Entities.QrCode Planted(string id, string pole, string commune, string by, DateTime at, bool revoked) => new()
    {
        QrId = id, Code = RandomCode(), PoleId = pole, CommuneId = commune, CreatedBy = by, CreatedAt = at,
        RevokedBy = revoked ? by : null, RevokedAt = revoked ? at : null,
    };

    private static string RandomCode()
        => System.Security.Cryptography.RandomNumberGenerator.GetString("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", 16);

    /// <summary>
    /// <c>FOR NO KEY UPDATE</c>, not <c>FOR UPDATE</c>: issuing inserts a row whose foreign key to <c>pole</c> takes KEY SHARE,
    /// which <c>FOR UPDATE</c> would block too — the test would pass with the service's lock removed (LIGHT-CTRL 2b lesson).
    /// </summary>
    private async Task HoldingThePoleLockAsync(string pole, Func<Task<HttpResponseMessage>> send, HttpStatusCode expected)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {pole} FOR NO KEY UPDATE");

        var request = send();
        await Task.WhenAny(request, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(request.IsCompleted, "the request finished while the pole row was locked — it never took the lock");

        await transaction.RollbackAsync();
        var response = await request;
        Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read(response);
    }

    private static async Task<string?> Code(HttpResponseMessage response)
        => (await Read(response)).GetProperty("error").GetProperty("code").GetString();

    private Task<string> PlantPoleAsync(string commune)
        => factory.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var road = new RoadSegment
            {
                CommuneId = commune, SegmentName = "QR road", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(107.4, 11.3), new Coordinate(107.41, 11.31)]) { SRID = 4326 }, LengthM = 100,
            };
            db.Add(road);
            await db.SaveChangesAsync();
            var pole = new Pole { CommuneId = commune, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.405, 11.305) { SRID = 4326 } };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
}
