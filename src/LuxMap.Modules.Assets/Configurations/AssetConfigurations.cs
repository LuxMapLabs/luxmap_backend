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
internal static class GeometryColumns
{
    public const string GistMethod = "gist";

    public static readonly string Point = $"geometry(Point,{SpatialConstants.Srid})";

    public static readonly string LineString = $"geometry(LineString,{SpatialConstants.Srid})";
}

/// <summary>
/// <c>external_ref</c> — the owning authority's inventory code, and the schema's only natural key.
/// </summary>
/// <remarks>
/// One place rather than three copies, because the three declarations must not drift: the import
/// upsert keys on exactly this index, so a differing filter or column order on one table would make
/// that table silently non-idempotent while the other two stayed correct.
/// <para>
/// The index is PARTIAL (<c>WHERE external_ref IS NOT NULL</c>) and that is load-bearing, not a
/// refinement: in PostgreSQL a plain unique index treats NULLs as distinct, so it would technically
/// work — but the partial form states the intent, keeps the index off every code-less row traced
/// from public imagery, and is what BE-12a's upsert lookup rides.
/// </para>
/// </remarks>
internal static class ExternalRefColumn
{
    public const string NotNullFilter = "external_ref IS NOT NULL";

    public static void HasExternalRef<TEntity>(this EntityTypeBuilder<TEntity> builder, string table)
        where TEntity : class, IExternallyReferenced
    {
        builder.Property(entity => entity.ExternalRef).HasColumnType("text");

        builder.HasIndex("CommuneId", nameof(IExternallyReferenced.ExternalRef))
            .HasDatabaseName($"ux_{table}_commune_external_ref")
            .HasFilter(NotNullFilter)
            .IsUnique();

        // ⚠️ Re-declares ix_<table>_commune_id, which EF Core creates by CONVENTION for the
        // administrative_unit foreign key. Convention SKIPS that index once another one leads with
        // the same column — and the index above does. Without this line the migration DROPS
        // ix_pole_commune_id, ix_road_segment_commune_id and ix_feeder_commune_id, which would be a
        // silent regression on every single query in the system: the index left standing is PARTIAL,
        // so it does not cover the rows where external_ref IS NULL, and those are the majority
        // (everything traced from public imagery). Meanwhile the BE-08 query filter puts commune_id
        // into the WHERE clause of every read. Verified by generating the migration without it.
        builder.HasIndex("CommuneId");
    }
}

public sealed class RoadSegmentConfiguration : IEntityTypeConfiguration<RoadSegment>
{
    public void Configure(EntityTypeBuilder<RoadSegment> builder)
    {
        builder.ToTable("road_segment");
        builder.HasKey(segment => segment.SegmentId);

        builder.Property(segment => segment.SegmentId).HasPrefixedId(PrefixedIds.RoadSegment);
        builder.Property(segment => segment.SegmentName).HasColumnType("text").IsRequired();
        builder.HasExternalRef("road_segment");
        builder.Property(segment => segment.LengthM).IsRequired();
        builder.Property(segment => segment.Geom).HasColumnType(GeometryColumns.LineString).IsRequired();
        builder.Property(segment => segment.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(segment => segment.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(segment => segment.RoadClass);
        builder.HasContractEnum(segment => segment.DataSource);

        builder.HasCommuneScope();

        // Contract section 5.3: every geometry column carries a GIST index, and the bbox queries must
        // ride it rather than scanning.
        builder.HasIndex(segment => segment.Geom).HasMethod(GeometryColumns.GistMethod);

        builder.HasCommuneReference(segment => segment.CommuneId);
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
        builder.HasExternalRef("feeder");
        // The only nullable geometry in the module — Branch C never surveyed the cable routes.
        builder.Property(feeder => feeder.Geom).HasColumnType(GeometryColumns.LineString);
        builder.Property(feeder => feeder.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(feeder => feeder.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasCommuneScope();

        // Still indexed although the column is nullable: PostgreSQL simply leaves NULL rows out.
        builder.HasIndex(feeder => feeder.Geom).HasMethod(GeometryColumns.GistMethod);

        builder.HasCommuneReference(feeder => feeder.CommuneId);
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
        builder.HasExternalRef("pole");
        builder.Property(pole => pole.FeederId).HasColumnType("text");
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

        builder.HasCommuneReference(pole => pole.CommuneId);
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

        builder.HasCommuneReference(fixture => fixture.CommuneId);

        // "The lamp currently in service on this pole" is the lookup BE-14 makes for every pole in a
        // bbox, so it gets its own partial index rather than filtering the full history each time.
        //
        // ⚠️ NOT UNIQUE, and it carries NO business rule. It has already been misread once as
        // "a pole has at most one active fixture" — it does not say that, and the opposite is the
        // settled rule: a pole carries several lamps, and replacing one keeps the old row with a
        // removed_date (see Fixture, and docs/templates/README.md). This is a lookup index, nothing
        // more. That absence of a natural key is exactly why CSV import treats fixtures as
        // INSERT-ONLY instead of upserting them.
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

        builder.Property(status => status.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(status => status.FixtureStatus);

        builder.HasCommuneScope();

        builder.HasOne(status => status.Pole)
            .WithOne(pole => pole.CurrentStatus)
            .HasForeignKey<PoleCurrentStatus>(status => status.PoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasCommuneReference(status => status.CommuneId);

        // The invariant the mock set exhibits on all 103 poles: confidence is absent exactly when the
        // status is `unknown`. Enforced in BOTH directions — no confidence without an observation, no
        // observation without a confidence.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pole_current_status_confidence_matches_status",
            "(status_confidence IS NULL) = (fixture_status = 'unknown')"));
    }
}
