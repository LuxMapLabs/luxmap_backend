using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// Every <c>commune_id</c> must be a real foreign key to <c>administrative_unit</c>.
/// <para>
/// The reason is not tidiness. An orphaned <c>commune_id</c> throws nothing: BE-08's filter is in
/// the <c>WHERE</c> clause, so the row is simply invisible to every user at once, with no exception
/// and no log line. One mistyped commune in a BE-12 CSV import would make assets vanish with nothing
/// to point at the cause.
/// </para>
/// </summary>
[Collection(nameof(AssetSchemaCollection))]
public class CommuneReferenceTests(AssetSchemaFixture fixture)
{
    [Fact]
    public async Task Every_entity_with_a_commune_id_column_has_the_foreign_key()
    {
        // The same invariant the startup guard enforces, asserted against the model that actually
        // ships. The guard scans COLUMNS rather than the ICommuneScoped interface, which is what
        // closes the hole that interface's own documentation admits: an entity carrying commune_id
        // but forgetting to implement it used to slip past everything but code review.
        var unreferenced = await fixture.QueryAsync(db => Task.FromResult(
            db.Model.GetEntityTypes()
                .Where(entity => entity.ClrType != typeof(AdministrativeUnit))
                .Where(entity => entity.GetProperties().Any(p =>
                    p.GetColumnName() == CommuneReferenceBuilderExtensions.CommuneColumn))
                .Where(entity => !entity.GetForeignKeys().Any(fk =>
                    fk.PrincipalEntityType.ClrType == typeof(AdministrativeUnit)))
                .Select(entity => entity.ClrType.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()));

        Assert.Empty(unreferenced);
    }

    [Fact]
    public async Task Entities_carrying_commune_id_are_more_than_a_token_few()
    {
        // Guards that silently match nothing are worse than no guard, so pin that the scan really is
        // covering the asset tables rather than passing on an empty set.
        var scoped = await fixture.QueryAsync(db => Task.FromResult(
            db.Model.GetEntityTypes()
                .Where(entity => entity.ClrType != typeof(AdministrativeUnit))
                .Count(entity => entity.GetProperties().Any(p =>
                    p.GetColumnName() == CommuneReferenceBuilderExtensions.CommuneColumn))));

        // pole, fixture, road_segment, feeder, pole_current_status, app_user_commune — at minimum.
        Assert.True(scoped >= 6, $"Only {scoped} entities carry commune_id; expected at least 6.");
    }

    [Fact]
    public async Task An_unknown_commune_is_rejected_by_the_database()
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            db.Set<Pole>().Add(new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = "COM-999",
                Geom = new Point(106.49, 10.97) { SRID = 4326 },
                DataSource = DataSource.PublicImagery,
            });

            return await db.SaveChangesAsync();
        }));

        // 23503 = foreign_key_violation. Before the constraint this insert SUCCEEDED and produced a
        // pole no user could ever see.
        Assert.Contains("23503", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_commune_still_holding_assets_cannot_be_deleted()
    {
        // Restrict, never cascade: deleting an administrative unit must not take its poles with it.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            var commune = await db.Set<AdministrativeUnit>()
                .SingleAsync(unit => unit.CommuneId == fixture.CommuneId);

            db.Set<AdministrativeUnit>().Remove(commune);
            return await db.SaveChangesAsync();
        }));

        Assert.Contains("23503", error.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);

        // And the poles are still there.
        var survivors = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .CountAsync(pole => pole.CommuneId == fixture.CommuneId));

        Assert.True(survivors >= AssetSchemaFixture.SyntheticPoleCount);
    }

    [Fact]
    public async Task The_anchor_table_is_not_itself_commune_scoped()
    {
        // AdministrativeUnit defines the scope; filtering it BY that scope would be circular — the
        // row describing a commune would be hidden by the scope derived from it. Endpoints listing
        // communes query the commune_ids claim explicitly instead.
        Assert.False(typeof(AdministrativeUnit).IsAssignableTo(typeof(ICommuneScoped)));

        // Proof it is genuinely unfiltered: this context has no HTTP request and therefore an empty
        // scope, which hides every row of every scoped entity. The anchor still answers.
        var visibleCommunes = await fixture.QueryAsync(db => db.Set<AdministrativeUnit>().CountAsync());
        var visiblePoles = await fixture.QueryAsync(db => db.Set<Pole>().CountAsync());

        Assert.True(visibleCommunes > 0);
        Assert.Equal(0, visiblePoles);
    }
}
