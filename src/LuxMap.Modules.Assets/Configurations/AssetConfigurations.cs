using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Assets.Configurations;

/// <summary>
/// The geometry column types of Contract section 5.3. Written once here so no migration ever carries
/// a hand-typed SRID.
/// </summary>
/// <remarks>
/// <b>Why <c>commune_id</c> carries an index but no foreign key.</b> <c>administrative_unit</c> is
/// owned by the Identity module, so a real FK would make Assets depend on Identity — and every later
/// module (Faults, Survey, Telemetry, WorkOrders) would inherit the same dependency, since they all
/// carry <c>commune_id</c> too. That is a decision about module boundaries, not about this table, so
/// it is left open rather than settled here.
/// <para>
/// ⚠️ The gap is real: a bad <c>commune_id</c> (a typo in the BE-12 CSV import, say) produces rows
/// that the section 7 query filter hides from EVERY user, with nothing to point at the cause. Adding
/// the constraint later is one small migration; unwinding a module dependency is not, which is the
/// only reason this is the safer order.
/// </para>
/// </remarks>
internal static class GeometryColumns
{
    public const string GistMethod = "gist";

    public static readonly string Point = $"geometry(Point,{SpatialConstants.Srid})";

    public static readonly string LineString = $"geometry(LineString,{SpatialConstants.Srid})";
}

public sealed class RoadSegmentConfiguration : IEntityTypeConfiguration<RoadSegment>
{
    public void Configure(EntityTypeBuilder<RoadSegment> builder)
    {
        builder.ToTable("road_segment");
        builder.HasKey(segment => segment.SegmentId);

        builder.Property(segment => segment.SegmentId).HasPrefixedId(PrefixedIds.RoadSegment);
        builder.Property(segment => segment.SegmentName).HasColumnType("text").IsRequired();
        builder.Property(segment => segment.LengthM).IsRequired();
        builder.Property(segment => segment.Geom).HasColumnType(GeometryColumns.LineString).IsRequired();
        builder.Property(segment => segment.CommuneId).HasColumnType("text").IsRequired();
        builder.Property(segment => segment.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(segment => segment.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(segment => segment.RoadClass);
        builder.HasContractEnum(segment => segment.DataSource);

        builder.HasCommuneScope();

        // Contract section 5.3: every geometry column carries a GIST index, and the bbox queries must
        // ride it rather than scanning.
        builder.HasIndex(segment => segment.Geom).HasMethod(GeometryColumns.GistMethod);

        // Indexed, NOT a foreign key — see the CommuneId note on GeometryColumns.
        builder.HasIndex(segment => segment.CommuneId);
    }
}

public sealed class FeederConfiguration : IEntityTypeConfiguration<Feeder>
{
    public void Configure(EntityTypeBuilder<Feeder> builder)
    {
        builder.ToTable("feeder");
        builder.HasKey(feeder => feeder.FeederId);

        builder.Property(feeder => feeder.FeederId).HasPrefixedId(PrefixedIds.Feeder);
        builder.Property(feeder => feeder.FeederName).HasColumnType("text").IsRequired();
        builder.Property(feeder => feeder.CommuneId).HasColumnType("text").IsRequired();
        // The only nullable geometry in the module — Branch C never surveyed the cable routes.
        builder.Property(feeder => feeder.Geom).HasColumnType(GeometryColumns.LineString);
        builder.Property(feeder => feeder.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(feeder => feeder.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasCommuneScope();

        // Still indexed although the column is nullable: PostgreSQL simply leaves NULL rows out.
        builder.HasIndex(feeder => feeder.Geom).HasMethod(GeometryColumns.GistMethod);

        // Indexed, NOT a foreign key — see the CommuneId note on GeometryColumns.
        builder.HasIndex(feeder => feeder.CommuneId);
    }
}

public sealed class PoleConfiguration : IEntityTypeConfiguration<Pole>
{
    public void Configure(EntityTypeBuilder<Pole> builder)
    {
        builder.ToTable("pole");
        builder.HasKey(pole => pole.PoleId);

        builder.Property(pole => pole.PoleId).HasPrefixedId(PrefixedIds.Pole);
        builder.Property(pole => pole.SegmentId).HasColumnType("text").IsRequired();
        builder.Property(pole => pole.FeederId).HasColumnType("text");
        builder.Property(pole => pole.CommuneId).HasColumnType("text").IsRequired();
        builder.Property(pole => pole.Geom).HasColumnType(GeometryColumns.Point).IsRequired();
        builder.Property(pole => pole.NearSensitivePoi).HasDefaultValue(false);
        builder.Property(pole => pole.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(pole => pole.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(pole => pole.DataSource);

        builder.HasCommuneScope();

        // The index the whole task turns on: GET /poles must answer a bbox in under 500 ms at 2000
        // poles, and that only happens through ST_Intersects on this index.
        builder.HasIndex(pole => pole.Geom).HasMethod(GeometryColumns.GistMethod);

        builder.HasOne(pole => pole.Segment)
            .WithMany(segment => segment.Poles)
            .HasForeignKey(pole => pole.SegmentId)
            // Deleting a segment that still carries poles would orphan the assets.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pole => pole.Feeder)
            .WithMany(feeder => feeder.Poles)
            .HasForeignKey(pole => pole.FeederId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexed, NOT a foreign key — see the CommuneId note on GeometryColumns.
        builder.HasIndex(pole => pole.CommuneId);
    }
}

public sealed class FixtureConfiguration : IEntityTypeConfiguration<Fixture>
{
    public void Configure(EntityTypeBuilder<Fixture> builder)
    {
        builder.ToTable("fixture");
        builder.HasKey(fixture => fixture.FixtureId);

        builder.Property(fixture => fixture.FixtureId).HasPrefixedId(PrefixedIds.Fixture);
        builder.Property(fixture => fixture.PoleId).HasColumnType("text").IsRequired();
        builder.Property(fixture => fixture.CommuneId).HasColumnType("text").IsRequired();
        builder.Property(fixture => fixture.LampWatt).IsRequired();

        // Contract section 0 separates dates from timestamps: these three are DATE, never TIMESTAMPTZ.
        builder.Property(fixture => fixture.InstallDate).HasColumnType("date").IsRequired();
        builder.Property(fixture => fixture.RemovedDate).HasColumnType("date");
        builder.Property(fixture => fixture.WarrantyExpiry).HasColumnType("date");

        builder.Property(fixture => fixture.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(fixture => fixture.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(fixture => fixture.FixtureType);
        builder.HasContractEnum(fixture => fixture.PowerSource);
        builder.HasContractEnum(fixture => fixture.DataSource);

        builder.HasCommuneScope();

        builder.HasOne(fixture => fixture.Pole)
            .WithMany(pole => pole.Fixtures)
            .HasForeignKey(fixture => fixture.PoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexed, NOT a foreign key — see the CommuneId note on GeometryColumns.
        builder.HasIndex(fixture => fixture.CommuneId);

        // "The lamp currently in service on this pole" is the lookup BE-14 makes for every pole in a
        // bbox, so it gets its own partial index rather than filtering the full history each time.
        builder.HasIndex(fixture => fixture.PoleId)
            .HasDatabaseName("ix_fixture_pole_id_active")
            .HasFilter("removed_date IS NULL");
    }
}

public sealed class PoleCurrentStatusConfiguration : IEntityTypeConfiguration<PoleCurrentStatus>
{
    public void Configure(EntityTypeBuilder<PoleCurrentStatus> builder)
    {
        builder.ToTable("pole_current_status");
        builder.HasKey(status => status.PoleId);

        // No HasPrefixedId: this table has no display ID of its own, it is keyed by the pole.
        builder.Property(status => status.PoleId).HasColumnType("text");
        builder.Property(status => status.StatusConfidence).HasColumnType("double precision");
        builder.Property(status => status.LastSeenAt);

        // Deliberately plain text and no foreign key — survey_sweep arrives with BE-15, which adds the
        // constraint in its own migration. The one deferred FK in BE-09.
        builder.Property(status => status.LastSweepId).HasColumnType("text");

        builder.Property(status => status.CommuneId).HasColumnType("text").IsRequired();
        builder.Property(status => status.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(status => status.FixtureStatus);

        builder.HasCommuneScope();

        builder.HasOne(status => status.Pole)
            .WithOne(pole => pole.CurrentStatus)
            .HasForeignKey<PoleCurrentStatus>(status => status.PoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexed, NOT a foreign key — see the CommuneId note on GeometryColumns.
        builder.HasIndex(status => status.CommuneId);

        // The invariant the mock set exhibits on all 103 poles: confidence is absent exactly when the
        // status is `unknown`. Enforced in BOTH directions — no confidence without an observation, no
        // observation without a confidence.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pole_current_status_confidence_matches_status",
            "(status_confidence IS NULL) = (fixture_status = 'unknown')"));
    }
}
