using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-09 — the four asset entities plus <c>pole_current_status</c>, against a real PostGIS database.
/// The point of the task is that geometry survives the round trip and that <c>bbox</c> queries ride
/// the GIST index, and neither can be verified without the real database.
/// </summary>
[Collection(nameof(AssetSchemaCollection))]
public class AssetSchemaTests(AssetSchemaFixture fixture)
{
    private const int Srid = 4326;

    [Fact]
    public async Task Pole_id_and_segment_id_are_generated_by_the_database_in_contract_format()
    {
        // Neither insert assigns an ID — Contract section 0.4 says the database does it.
        Assert.StartsWith("SEG-", fixture.SegmentId, StringComparison.Ordinal);

        var poleId = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .Where(pole => pole.CommuneId == fixture.CommuneId)
            .Select(pole => pole.PoleId)
            .FirstAsync());

        Assert.StartsWith("POLE-", poleId, StringComparison.Ordinal);

        // Section 0.1: four digits for a high-volume entity, three for a low-volume one.
        Assert.Matches(@"^POLE-\d{4,}$", poleId);
        Assert.Matches(@"^SEG-\d{3,}$", fixture.SegmentId);
    }

    [Fact]
    public async Task A_point_written_through_ef_core_reads_back_with_the_same_coordinates_and_srid()
    {
        var written = new Point(106.492025, 10.965989) { SRID = Srid };

        var poleId = await fixture.QueryAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = written,
                DataSource = DataSource.CalibrationRig,
            };

            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        var read = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .Where(pole => pole.PoleId == poleId)
            .Select(pole => pole.Geom)
            .SingleAsync());

        Assert.Equal(Srid, read.SRID);
        Assert.Equal(written.X, read.X, precision: 9);
        Assert.Equal(written.Y, read.Y, precision: 9);

        // GeoJSON order is [lng, lat]: X is the longitude, and mixing them up puts every pole in the
        // wrong hemisphere while every test still passes.
        Assert.Equal(106.492025, read.X, precision: 9);
        Assert.Equal(10.965989, read.Y, precision: 9);
    }

    [Fact]
    public async Task A_linestring_written_through_ef_core_reads_back_with_every_vertex_intact()
    {
        var written = new LineString(
        [
            new Coordinate(106.49, 10.97),
            new Coordinate(106.495, 10.97),
            new Coordinate(106.504641, 10.97),
        ])
        { SRID = Srid };

        var segmentId = await fixture.QueryAsync(async db =>
        {
            var segment = new RoadSegment
            {
                SegmentName = $"Round trip {Guid.NewGuid():N}",
                RoadClass = RoadClass.InterVillage,
                LengthM = 1600,
                Geom = written,
                CommuneId = fixture.CommuneId,
                DataSource = DataSource.PublicImagery,
            };

            db.Set<RoadSegment>().Add(segment);
            await db.SaveChangesAsync();
            return segment.SegmentId;
        });

        var read = await fixture.QueryAsync(db => db.Set<RoadSegment>()
            .IgnoreQueryFilters()
            .Where(segment => segment.SegmentId == segmentId)
            .Select(segment => segment.Geom)
            .SingleAsync());

        Assert.Equal(Srid, read.SRID);
        Assert.Equal(3, read.NumPoints);
        Assert.Equal(written.Coordinates.Length, read.Coordinates.Length);
        for (var i = 0; i < written.Coordinates.Length; i++)
        {
            Assert.Equal(written.Coordinates[i].X, read.Coordinates[i].X, precision: 9);
            Assert.Equal(written.Coordinates[i].Y, read.Coordinates[i].Y, precision: 9);
        }
    }

    [Fact]
    public async Task A_feeder_stores_no_geometry_at_all()
    {
        // Branch C never surveyed the cable routes, so a feeder with no route must still be storable.
        var feederId = await fixture.QueryAsync(async db =>
        {
            var feeder = new Feeder
            {
                FeederName = $"Feeder {Guid.NewGuid():N}"[..20],
                CommuneId = fixture.CommuneId,
                Geom = null,
            };

            db.Set<Feeder>().Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

        Assert.Matches(@"^FDR-\d{3,}$", feederId);

        var geom = await fixture.QueryAsync(db => db.Set<Feeder>()
            .IgnoreQueryFilters()
            .Where(feeder => feeder.FeederId == feederId)
            .Select(feeder => feeder.Geom)
            .SingleAsync());

        Assert.Null(geom);
    }

    [Fact]
    public async Task Install_date_stays_a_date_and_survives_without_a_time_component()
    {
        var poleId = await NewPoleAsync();

        var fixtureId = await fixture.QueryAsync(async db =>
        {
            var lamp = new Fixture
            {
                PoleId = poleId,
                CommuneId = fixture.CommuneId,
                FixtureType = FixtureType.SolarAllInOne,
                PowerSource = PowerSource.Solar,
                LampWatt = 60,
                InstallDate = new DateOnly(2023, 1, 4),
                WarrantyExpiry = new DateOnly(2028, 1, 4),
                DataSource = DataSource.PublicImagery,
            };

            db.Set<Fixture>().Add(lamp);
            await db.SaveChangesAsync();
            return lamp.FixtureId;
        });

        Assert.Matches(@"^FIX-\d{4,}$", fixtureId);

        var read = await fixture.QueryAsync(db => db.Set<Fixture>()
            .IgnoreQueryFilters()
            .Where(lamp => lamp.FixtureId == fixtureId)
            .Select(lamp => new { lamp.InstallDate, lamp.WarrantyExpiry, lamp.RemovedDate })
            .SingleAsync());

        Assert.Equal(new DateOnly(2023, 1, 4), read.InstallDate);
        Assert.Equal(new DateOnly(2028, 1, 4), read.WarrantyExpiry);

        // Still in service: this is what the partial index keys on.
        Assert.Null(read.RemovedDate);
    }

    [Fact]
    public async Task A_pole_can_carry_two_fixtures_and_replacing_one_keeps_the_history()
    {
        var poleId = await NewPoleAsync();

        await fixture.QueryAsync(async db =>
        {
            db.Set<Fixture>().AddRange(
                NewFixture(poleId, 100, removed: new DateOnly(2025, 6, 1)),
                NewFixture(poleId, 80, removed: null));
            return await db.SaveChangesAsync();
        });

        var lamps = await fixture.QueryAsync(db => db.Set<Fixture>()
            .IgnoreQueryFilters()
            .Where(lamp => lamp.PoleId == poleId)
            .ToListAsync());

        // Pole and Fixture are separate tables: the pole stands while its lamp is replaced, and the
        // replaced lamp stays on record.
        Assert.Equal(2, lamps.Count);
        Assert.Single(lamps, lamp => lamp.RemovedDate is null);
        Assert.Single(lamps, lamp => lamp.RemovedDate is not null);
    }

    [Fact]
    public async Task Status_confidence_is_absent_exactly_when_the_status_is_unknown()
    {
        var poleId = await NewPoleAsync();

        // `unknown` means the sweep could not cover the pole, so there is no confidence to record.
        await fixture.QueryAsync(async db =>
        {
            db.Set<PoleCurrentStatus>().Add(new PoleCurrentStatus
            {
                PoleId = poleId,
                CommuneId = fixture.CommuneId,
                FixtureStatus = FixtureStatus.Unknown,
                StatusConfidence = null,
            });
            return await db.SaveChangesAsync();
        });

        // The other direction: an observed status without a confidence must be rejected by the CHECK.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            var status = await db.Set<PoleCurrentStatus>()
                .IgnoreQueryFilters()
                .SingleAsync(row => row.PoleId == poleId);

            status.FixtureStatus = FixtureStatus.Dim;
            status.StatusConfidence = null;
            return await db.SaveChangesAsync();
        }));

        Assert.Contains("ck_pole_current_status_confidence_matches_status", error.InnerException?.Message);
    }

    [Fact]
    public async Task Unknown_carrying_a_confidence_is_rejected_too()
    {
        var poleId = await NewPoleAsync();

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            db.Set<PoleCurrentStatus>().Add(new PoleCurrentStatus
            {
                PoleId = poleId,
                CommuneId = fixture.CommuneId,
                FixtureStatus = FixtureStatus.Unknown,
                StatusConfidence = 0.9,
            });
            return await db.SaveChangesAsync();
        }));

        Assert.Contains("ck_pole_current_status_confidence_matches_status", error.InnerException?.Message);
    }

    [Theory]
    [InlineData(typeof(Pole))]
    [InlineData(typeof(Fixture))]
    [InlineData(typeof(RoadSegment))]
    [InlineData(typeof(Feeder))]
    [InlineData(typeof(PoleCurrentStatus))]
    public void Every_asset_entity_is_commune_scoped(Type entityType)
        => Assert.True(
            entityType.IsAssignableTo(typeof(ICommuneScoped)),
            $"{entityType.Name} carries commune_id but does not implement ICommuneScoped, so the "
            + "BE-08 guard cannot see it and Contract section 7 filtering would be silently skipped.");

    [Fact]
    public async Task Without_a_commune_scope_the_filter_hides_every_asset_row()
    {
        // No HTTP request means an empty scope, and an empty scope must reveal NOTHING — the same
        // fail-closed behaviour BE-08 relies on. If this ever returns rows, the filter came off.
        var visible = await fixture.QueryAsync(db => db.Set<Pole>().CountAsync());
        Assert.Equal(0, visible);

        var actual = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().CountAsync());
        Assert.True(actual >= AssetSchemaFixture.SyntheticPoleCount);
    }

    private async Task<string> NewPoleAsync()
        => await fixture.QueryAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(106.49, 10.97) { SRID = Srid },
                DataSource = DataSource.PublicImagery,
            };

            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private Fixture NewFixture(string poleId, int watt, DateOnly? removed) => new()
    {
        PoleId = poleId,
        CommuneId = fixture.CommuneId,
        FixtureType = FixtureType.LedRoadLamp,
        PowerSource = PowerSource.Grid,
        LampWatt = watt,
        InstallDate = new DateOnly(2022, 3, 24),
        RemovedDate = removed,
        DataSource = DataSource.PublicImagery,
    };
}
