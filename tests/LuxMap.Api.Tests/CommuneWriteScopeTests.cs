using System.Security.Claims;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-08 hotfix — Contract section 7 applied to WRITES.
/// </summary>
/// <remarks>
/// Until this guard existed, <c>HasQueryFilter</c> covered reads only: <c>Add</c>, <c>Update</c> and
/// <c>Remove</c> went to the database unchecked. The acceptance criterion for BE-08 said an engineer
/// "only SEES assets in their own commune", and reading is all anyone tested — this file is what
/// makes the write half true as well.
/// <para>
/// The scope is set the way production sets it: a real <see cref="ClaimsPrincipal"/> on
/// <see cref="IHttpContextAccessor"/>, which is what <c>CommuneScopeAccessor</c> reads. No test seam,
/// no substitute accessor — the object under test is the one that runs.
/// </para>
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class CommuneWriteScopeTests(AssetSchemaFixture fixture) : IAsyncLifetime
{
    private const int Srid = 4326;

    /// <summary>
    /// A REAL commune the signed-in user is not scoped to.
    /// </summary>
    /// <remarks>
    /// It has to be real. A made-up id is stopped by <c>fk_pole_administrative_unit_commune_id</c>
    /// before the guard is even reached, and the test would then be proving the foreign key rather
    /// than the authorization — which is exactly the confusion this hotfix exists to clear up. The
    /// foreign key proves a commune EXISTS; it says nothing about who may write to it.
    /// </remarks>
    private string ForeignCommune => foreignCommune.Value;

    private readonly Lazy<string> foreignCommune = new(() => CreateForeignCommune(fixture));

    /// <summary>
    /// <c>AdministrativeUnit</c> is deliberately NOT <c>ICommuneScoped</c> — it is the anchor, not a
    /// scoped resource — so creating one needs no backdoor.
    /// </summary>
    private static string CreateForeignCommune(AssetSchemaFixture fixture)
        => fixture.QueryAsync(async db =>
        {
            var unit = new AdministrativeUnit { Name = $"Foreign commune {Guid.NewGuid():N}"[..40] };
            db.Set<AdministrativeUnit>().Add(unit);
            await db.SaveChangesAsync();
            return unit.CommuneId;
        }).GetAwaiter().GetResult();

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Restores the ambient context and removes the rows this class created.
    /// </summary>
    /// <remarks>
    /// The cleanup is not optional. <see cref="AssetSchemaFixture"/> deletes only ITS OWN commune, so
    /// poles left behind in the foreign commune still reference its <c>road_segment</c> and the
    /// fixture's teardown then fails on the foreign key. A test that dirties a shared database has to
    /// clear up after itself.
    /// </remarks>
    public async Task DisposeAsync()
    {
        Accessor().HttpContext = null;

        if (!foreignCommune.IsValueCreated)
        {
            return;
        }

        var commune = foreignCommune.Value;

        await fixture.WriteAsSystemAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            await db.Set<Pole>().IgnoreQueryFilters()
                .Where(pole => pole.CommuneId == commune).ExecuteDeleteAsync();

            return await db.Set<AdministrativeUnit>()
                .Where(unit => unit.CommuneId == commune).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });
    }

    private IHttpContextAccessor Accessor()
        => fixture.Services.GetRequiredService<IHttpContextAccessor>();

    /// <summary>Puts a real principal on the ambient HttpContext, exactly as authentication would.</summary>
    private void SignInWith(params string[] communeIds)
    {
        var claims = new List<Claim>
        {
            new(AuthClaims.Subject, "USR-001"),
            new(AuthClaims.Role, ContractEnum.ToDbValue(UserRole.MaintenanceEngineer)),
        };

        claims.AddRange(communeIds.Select(id => new Claim(AuthClaims.CommuneIds, id)));

        Accessor().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
    }

    private void SignInAsAdministrator()
    {
        Accessor().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(AuthClaims.Subject, "USR-000"),
                    new Claim(AuthClaims.Role, ContractEnum.ToDbValue(UserRole.Administrator)),
                    new Claim(AuthClaims.CommuneIds, AuthClaims.AllCommunes),
                ],
                authenticationType: "Test")),
        };
    }

    private Pole NewPole(string communeId) => new()
    {
        SegmentId = fixture.SegmentId,
        CommuneId = communeId,
        Geom = new Point(106.49, 10.97) { SRID = Srid },
        DataSource = DataSource.PublicImagery,
    };

    // ── (i) Add outside the scope ────────────────────────────────────────────

    [Fact]
    public async Task Adding_a_pole_for_another_commune_is_refused_and_nothing_is_written()
    {
        SignInWith(fixture.CommuneId);

        var before = await CountForeignPolesAsync();

        var error = await Assert.ThrowsAsync<LuxMapException>(() => fixture.QueryAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(ForeignCommune));
            return await db.SaveChangesAsync();
        }));

        Assert.Equal(ErrorCodes.CommuneForbidden, error.Code);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);

        // The row must not exist. Before the guard this insert SUCCEEDED and the row then became
        // invisible to its own author — data gone with no exception and no log line.
        Assert.Equal(before, await CountForeignPolesAsync());
    }

    // ── (ii) Add inside the scope ────────────────────────────────────────────

    [Fact]
    public async Task Adding_a_pole_for_a_commune_in_the_claim_succeeds()
    {
        SignInWith(fixture.CommuneId);

        var written = await fixture.QueryAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(fixture.CommuneId));
            return await db.SaveChangesAsync();
        });

        Assert.Equal(1, written);
    }

    // ── (iii) and (iv) Modified, both directions ─────────────────────────────

    [Fact]
    public async Task Moving_a_pole_OUT_of_the_scope_is_refused()
    {
        SignInWith(fixture.CommuneId);
        var poleId = await AddPoleInScopeAsync();

        var error = await Assert.ThrowsAsync<LuxMapException>(() => fixture.QueryAsync(async db =>
        {
            var pole = await db.Set<Pole>().SingleAsync(p => p.PoleId == poleId);
            pole.CommuneId = ForeignCommune;
            return await db.SaveChangesAsync();
        }));

        Assert.Equal(ErrorCodes.CommuneForbidden, error.Code);
    }

    [Fact]
    public async Task Moving_a_pole_INTO_the_scope_is_refused_too()
    {
        // The mirror of the test above, and the reason the guard reads BOTH OriginalValues and
        // CurrentValues. Checking only the new value would let a caller adopt any row in the
        // database by renaming its commune to their own — a privilege escalation that looks, field
        // by field, like a perfectly in-scope write.
        var poleId = await AddForeignPoleAsSystemAsync();

        SignInWith(fixture.CommuneId);

        var error = await Assert.ThrowsAsync<LuxMapException>(() => fixture.QueryAsync(async db =>
        {
            var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
            pole.CommuneId = fixture.CommuneId;
            return await db.SaveChangesAsync();
        }));

        Assert.Equal(ErrorCodes.CommuneForbidden, error.Code);
    }

    // ── (v) Empty scope is a REFUSAL ─────────────────────────────────────────

    [Fact]
    public async Task An_empty_scope_refuses_the_write_rather_than_waving_it_through()
    {
        // No principal at all — the state of an unauthenticated caller, and equally of a token whose
        // commune_ids claim is missing or empty. The tempting shortcut is to read "no scope" as "no
        // restriction"; that would open the guard to exactly the callers it exists to stop.
        Accessor().HttpContext = null;

        var error = await Assert.ThrowsAsync<LuxMapException>(() => fixture.QueryAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(fixture.CommuneId));
            return await db.SaveChangesAsync();
        }));

        Assert.Equal(ErrorCodes.CommuneForbidden, error.Code);
    }

    // ── (vi) Administrator ───────────────────────────────────────────────────

    [Fact]
    public async Task An_administrator_carrying_the_wildcard_may_write_any_commune()
    {
        SignInAsAdministrator();

        var written = await fixture.QueryAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(fixture.CommuneId));
            return await db.SaveChangesAsync();
        });

        Assert.Equal(1, written);
    }

    // ── (vii) The backdoor ───────────────────────────────────────────────────

    [Fact]
    public async Task The_system_write_BACKDOOR_bypasses_the_guard_which_is_the_whole_point_of_it()
    {
        // This test exercises the DELIBERATE ESCAPE HATCH, not normal behaviour. It exists so the
        // backdoor's effect is pinned by a test rather than discovered later: seeding and fixture
        // setup write with no scope at all, and they are allowed to because they say so out loud.
        // If this test ever fails, the seeder and every asset fixture stop working — check what
        // opened or closed the hatch before changing anything else.
        Accessor().HttpContext = null;

        var written = await fixture.WriteAsSystemAsync(async db =>
        {
            db.Set<Pole>().Add(NewPole(ForeignCommune));
            return await db.SaveChangesAsync();
        });

        Assert.Equal(1, written);
    }

    private async Task<int> CountForeignPolesAsync()
        => await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .CountAsync(p => p.CommuneId == ForeignCommune));

    private async Task<string> AddPoleInScopeAsync()
        => await fixture.WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(fixture.CommuneId);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private async Task<string> AddForeignPoleAsSystemAsync()
        => await fixture.WriteAsSystemAsync(async db =>
        {
            var pole = NewPole(ForeignCommune);
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
}
