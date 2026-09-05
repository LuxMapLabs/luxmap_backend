using System.Security.Claims;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
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
/// BE-18 — the constraints that make a fault record trustworthy.
/// </summary>
/// <remarks>
/// The acceptance criterion is that every automatic detection and every engineer decision leaves a
/// record. Most of what protects that is in the schema, so the schema is what is tested: an
/// application-layer rule can be bypassed by CSV import, sync push or a seeder, and all three write
/// to this table.
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class FaultSchemaTests(AssetSchemaFixture fixture) : IAsyncLifetime
{
    private readonly List<string> faultIds = [];
    private readonly List<string> clusterIds = [];
    private readonly List<string> poleIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

        await fixture.WriteAsSystemAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            await db.Set<Fault>().IgnoreQueryFilters()
                .Where(f => faultIds.Contains(f.FaultId)).ExecuteDeleteAsync();
            await db.Set<FaultCluster>().IgnoreQueryFilters()
                .Where(c => clusterIds.Contains(c.ClusterId)).ExecuteDeleteAsync();
            return await db.Set<Pole>().IgnoreQueryFilters()
                .Where(p => poleIds.Contains(p.PoleId)).ExecuteDeleteAsync();
        });
    }

    /// <summary>Puts a real principal on the ambient context — what CommuneScopeAccessor reads.</summary>
    private void SignIn(params string[] communeIds)
    {
        var claims = new List<Claim>
        {
            new(AuthClaims.Subject, "USR-001"),
            new(AuthClaims.Role, ContractEnum.ToDbValue(UserRole.MaintenanceEngineer)),
        };
        claims.AddRange(communeIds.Select(id => new Claim(AuthClaims.CommuneIds, id)));

        fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
            };
    }

    private async Task<string> NewPoleAsync()
    {
        var poleId = await fixture.WriteAsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(106.49, 10.97) { SRID = 4326 },
                DataSource = DataSource.PublicImagery,
            };
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        poleIds.Add(poleId);
        return poleId;
    }

    private Fault NewFault(string? poleId, string communeId, double? lat = null, double? lng = null)
        => new()
        {
            PoleId = poleId,
            CommuneId = communeId,
            Lat = lat,
            Lng = lng,
            FaultType = FaultType.LampOut,
            FaultStatus = FaultStatus.Detected,
            Severity = Severity.Medium,
            SourceChannel = SourceChannel.Cv,
            DataSource = DataSource.PublicImagery,
            DetectedAt = DateTime.UtcNow,
        };

    private Task<int> WriteAsync(Fault fault, bool asSystem = true)
    {
        return asSystem
            ? fixture.WriteAsSystemAsync(async db => { db.Set<Fault>().Add(fault); return await db.SaveChangesAsync(); })
            : fixture.QueryAsync(async db => { db.Set<Fault>().Add(fault); return await db.SaveChangesAsync(); });
    }

    private void Track(Fault fault) => faultIds.Add(fault.FaultId);

    // ── (i) commune write guard ──────────────────────────────────────────────

    [Fact]
    public async Task A_fault_written_for_a_commune_outside_the_scope_is_refused_by_the_guard()
    {
        // Fault implements ICommuneScoped, so it inherits the BE-08 write guard with no extra code.
        // This test is what proves the interface was actually applied — forgetting it would leave
        // faults outside both the query filter and the guard, silently.
        SignIn(fixture.CommuneId);
        var poleId = await NewPoleAsync();

        var foreignCommune = await fixture.QueryAsync(async db =>
        {
            var unit = new AdministrativeUnit { Name = $"BE-18 foreign {Guid.NewGuid():N}"[..40] };
            db.Set<AdministrativeUnit>().Add(unit);
            await db.SaveChangesAsync();
            return unit.CommuneId;
        });

        var error = await Assert.ThrowsAsync<LuxMapException>(
            () => WriteAsync(NewFault(poleId, foreignCommune), asSystem: false));

        Assert.Equal(ErrorCodes.CommuneForbidden, error.Code);

        await fixture.WriteAsSystemAsync(db => db.Set<AdministrativeUnit>()
            .Where(u => u.CommuneId == foreignCommune).ExecuteDeleteAsync());
            #pragma warning restore RS0030
    }

    // ── (ii) and (iii) pole_id OR location ───────────────────────────────────

    [Fact]
    public async Task A_fault_with_neither_a_pole_nor_a_location_is_refused_by_the_database()
    {
        // LOCATION_REQUIRED exists for exactly this case (Contract 2.8). The rule is enforced in the
        // schema rather than only in the API, because a fault nobody can go and find is useless
        // whichever writer created it.
        var failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => WriteAsync(NewFault(poleId: null, fixture.CommuneId)));

        Assert.Contains("ck_fault_pole_or_location", failure.InnerException?.Message ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fault_with_no_pole_but_a_location_is_accepted()
    {
        // The field-report case: a crew reports a fault at a pole that is not in the asset records
        // yet, so there is a place but no pole_id.
        var fault = NewFault(poleId: null, fixture.CommuneId, lat: 10.9712, lng: 106.4983);

        Assert.Equal(1, await WriteAsync(fault));
        Track(fault);
    }

    // ── (iv) priority_score ──────────────────────────────────────────────────

    public static TheoryData<string, double> NotNumbers => new()
    {
        { "NaN", double.NaN },
        { "+Infinity", double.PositiveInfinity },
        { "-Infinity", double.NegativeInfinity },
    };

    [Theory]
    [MemberData(nameof(NotNumbers))]
    public async Task A_priority_score_that_is_not_a_number_is_refused(string label, double score)
    {
        // `>= 0` would not have caught these: PostgreSQL sorts NaN above every float. A non-numeric
        // score would sort into an arbitrary place in the default ordering and poison every average
        // BE-28 computes.
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        fault.PriorityScore = score;

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(fault));

        Assert.Contains("ck_fault_priority_score_finite", failure.InnerException?.Message ?? "", StringComparison.Ordinal);
        Assert.NotNull(label);
    }

    [Fact]
    public async Task A_null_priority_score_is_accepted_because_CV_16_may_not_have_run()
    {
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        fault.PriorityScore = null;

        Assert.Equal(1, await WriteAsync(fault));
        Track(fault);
    }

    // ── (v) status_confidence ────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(42.5)]
    public async Task A_status_confidence_outside_zero_to_one_is_refused(double confidence)
    {
        // pole_current_status.status_confidence accepts 42.5 today — its only check constrains
        // NULL-ness, not the value (drift 24). This column does not repeat that.
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        fault.StatusConfidence = confidence;

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(fault));

        Assert.Contains("ck_fault_status_confidence_range", failure.InnerException?.Message ?? "", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(0.92)]
    [InlineData(1d)]
    public async Task A_status_confidence_inside_the_range_or_absent_is_accepted(double? confidence)
    {
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        fault.StatusConfidence = confidence;

        Assert.Equal(1, await WriteAsync(fault));
        Track(fault);
    }

    // ── (vi) delete behaviour ────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_pole_that_carries_a_fault_is_refused_by_the_foreign_key()
    {
        // RESTRICT, not cascade. Removing a pole must not erase the record that its lamp was once
        // out — that record is the acceptance criterion of this ticket. It also keeps faults out of
        // the cascade blind spot in CLAUDE.md 1c, where the SaveChanges guard cannot see deletions
        // the database performs.
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        await WriteAsync(fault);
        Track(fault);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.WriteAsSystemAsync(async db =>
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
                db.Set<Pole>().Remove(pole);
                return await db.SaveChangesAsync();
            }));

        Assert.Contains("23503", failure.InnerException?.Message ?? "", StringComparison.Ordinal);
    }

    // ── (vii) cluster_id is a real foreign key ───────────────────────────────

    [Fact]
    public async Task A_cluster_id_pointing_at_no_cluster_is_refused()
    {
        // The reason cluster is a TABLE rather than a bare text column. As text, this row would have
        // been stored pointing at nothing, exactly like pole_current_status.last_sweep_id can today.
        var poleId = await NewPoleAsync();
        var fault = NewFault(poleId, fixture.CommuneId);
        fault.ClusterId = "CLS-999";

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => WriteAsync(fault));

        Assert.Contains("fk_fault_fault_cluster_cluster_id", failure.InnerException?.Message ?? "", StringComparison.Ordinal);
    }

    // ── (viii) generated ids ─────────────────────────────────────────────────

    [Fact]
    public async Task Both_ids_are_generated_by_the_database_in_contract_format()
    {
        var poleId = await NewPoleAsync();

        var cluster = new FaultCluster
        {
            SegmentId = fixture.SegmentId,
            CommuneId = fixture.CommuneId,
            ClusteredAt = DateTime.UtcNow,
        };

        await fixture.WriteAsSystemAsync(async db =>
        {
            db.Set<FaultCluster>().Add(cluster);
            return await db.SaveChangesAsync();
        });
        clusterIds.Add(cluster.ClusterId);

        var fault = NewFault(poleId, fixture.CommuneId);
        fault.ClusterId = cluster.ClusterId;
        await WriteAsync(fault);
        Track(fault);

        // Minimum width, not fixed width — Contract section 0.3.
        Assert.Matches(@"^FAULT-\d{4,}$", fault.FaultId);
        Assert.Matches(@"^CLS-\d{3,}$", cluster.ClusterId);
    }
}
