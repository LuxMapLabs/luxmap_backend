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

        // 🔴 REDUNDANT-LOOKING AND LOAD-BEARING. feeder_id is already the primary key, so uniqueness
        // over (feeder_id, commune_id) adds nothing on its own — and that is exactly why someone will
        // eventually delete it as dead weight. It is the TARGET of pole's composite foreign key
        // (O-7): PostgreSQL will only point a foreign key at columns carrying a unique constraint.
        // Dropping this drops the only thing making "a pole's circuit lives in the pole's commune" a
        // database rule instead of a habit. See PoleConfiguration.
        builder.HasAlternateKey(feeder => new { feeder.FeederId, feeder.CommuneId });

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

        // O-7 — the same-commune rule, as a CONSTRAINT rather than a call someone has to remember.
        //
        // The foreign key carries commune_id as well as feeder_id, so a pole can only hang off a
        // feeder whose commune matches its own. RequireFeederInCommuneAsync still runs first and
        // still owns the friendly 409 CROSS_COMMUNE_REFERENCE naming both communes; this is the
        // backstop underneath it, the same two-layer shape as CommuneFilter.Narrow over
        // CommuneWriteGuard. It covers the write paths that do not exist yet — BE-39's seeder,
        // BE-43's sync — and psql by hand, none of which will call the service method.
        //
        // ⚠️ MATCH SIMPLE is what makes a pole with no circuit legal, and it is the DEFAULT — do not
        // "tighten" it to MATCH FULL. Under MATCH SIMPLE a row with feeder_id NULL skips the check
        // entirely even though commune_id is non-null, which is the behaviour a solar_all_in_one pole
        // depends on. MATCH FULL would demand both columns be null together and reject every
        // feeder-less pole in the table. Pinned by
        // A_pole_with_no_feeder_is_still_legal_even_though_its_commune_is_not_null.
        //
        // Note segment_id deliberately gets NO such key: road_class = inter_commune means the road
        // runs BETWEEN communes, so a pole in a different commune from its segment's owner is
        // correct data, not a leak (BE-REVIEW-02, constraint 1).
        builder.HasOne(pole => pole.Feeder)
            .WithMany(feeder => feeder.Poles)
            .HasForeignKey(pole => new { pole.FeederId, pole.CommuneId })
            .HasPrincipalKey(feeder => new { feeder.FeederId, feeder.CommuneId })
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
        // UNIQUE since BE-REVIEW-02 (D-11): a pole carries AT MOST ONE lamp in service at a time.
        // That is now a business rule, decided by Dylan on 18/09/2026, and this index is what
        // enforces it. Three places used to disagree — import refused any pole that had EVER had a
        // lamp, CRUD allowed any number of active lamps, and BE-14 was going to flatten "the active
        // fixture" (singular). The history is untouched: replacing a lamp still keeps the old row
        // with a removed_date, and the index only sees rows where removed_date IS NULL. CV cannot
        // tell two lamps on one pole apart anyway (see Fixture), and every pole in the FO-26 mock set
        // carries exactly one.
        builder.HasIndex(fixture => fixture.PoleId)
            .IsUnique()
            .HasDatabaseName("ux_fixture_pole_id_active")
            .HasFilter("removed_date IS NULL");

        // A lamp cannot be taken down before it was put up (BE-REVIEW-02, Q-4). The API checks it
        // first to answer 400 with the field named; this is the backstop for every other write path.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_fixture_removed_after_install",
            "removed_date IS NULL OR removed_date >= install_date"));
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
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_pole_current_status_confidence_matches_status",
                "(status_confidence IS NULL) = (fixture_status = 'unknown')");

            // 0..1 and FINITE (BE-REVIEW-02, M-6 — drift 24). The XML doc promised 0..1 for months
            // while the column accepted 42.5, NaN and Infinity; a real INSERT proved it. NaN is the
            // silent one: PostgreSQL sorts it ABOVE every float, so `>= 0` and `<= 1` alone would
            // still let it in, and it is compared with `<> 'NaN'` because `x = x` is a tautology
            // here (NaN equals itself in PostgreSQL). Same three-term shape as lux_value and
            // fault.status_confidence. Sweep processing (BE-15/BE-17) owns the writes to this table;
            // the constraint is in place before the first row lands.
            table.HasCheckConstraint(
                "ck_pole_current_status_confidence_range",
                "status_confidence IS NULL OR (status_confidence >= 0 AND status_confidence <= 1 "
                + "AND status_confidence <> 'NaN'::float8 AND status_confidence <> 'Infinity'::float8 "
                + "AND status_confidence <> '-Infinity'::float8)");
        });
    }
}
