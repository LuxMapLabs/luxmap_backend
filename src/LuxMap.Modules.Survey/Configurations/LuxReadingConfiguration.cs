using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Survey.Configurations;

public sealed class LuxReadingConfiguration : IEntityTypeConfiguration<LuxReading>
{
    public void Configure(EntityTypeBuilder<LuxReading> builder)
    {
        builder.ToTable("lux_reading");
        builder.HasKey(reading => reading.LuxId);

        builder.Property(reading => reading.LuxId).HasPrefixedId(PrefixedIds.LuxReading);
        builder.Property(reading => reading.ClientOpId).HasColumnType("text").IsRequired();
        builder.Property(reading => reading.PoleId).HasColumnType("text").IsRequired();
        builder.Property(reading => reading.MeasuredBy).HasColumnType("text").IsRequired();
        builder.Property(reading => reading.MeterModel).HasColumnType("text");
        builder.Property(reading => reading.Note).HasColumnType("text");
        builder.Property(reading => reading.PhotoId).HasColumnType("text");

        // double precision, matching PoleCurrentStatus.StatusConfidence. A lux meter resolves to
        // about 0.1 lux and its own error dwarfs anything IEEE 754 introduces, so exact decimal
        // storage would buy accuracy the instrument never had — while costing the arithmetic CV-12
        // does over these values.
        builder.Property(reading => reading.LuxValue).HasColumnType("double precision").IsRequired();

        builder.Property(reading => reading.MeasuredAt).IsRequired();
        builder.Property(reading => reading.CreatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(reading => reading.DataSource);

        builder.HasCommuneScope();
        builder.HasCommuneReference(reading => reading.CommuneId);

        // Contract section 2.9: lux_value is a real number, non-negative. No upper bound — see the
        // remarks on LuxReading.LuxValue for why an implausible reading is kept rather than refused.
        //
        // ⚠️ The three terms are NOT redundant. `>= 0` alone lets NaN and Infinity through, because
        // PostgreSQL orders NaN ABOVE every other float — the opposite of IEEE 754, where every
        // comparison with NaN is false. So 'NaN' >= 0 and 'Infinity' >= 0 are both TRUE here.
        //
        //   lux_value <> 'NaN'         ⚠️ NOT `lux_value = lux_value`. That is the IEEE 754 idiom and
        //                              it does NOT work in PostgreSQL, which treats NaN as EQUAL to
        //                              itself — so `x = x` is a real tautology here and catches
        //                              nothing. Verified: SELECT 'NaN'::float8 = 'NaN'::float8 → t.
        //   lux_value <> 'Infinity'    -Infinity is already excluded by >= 0; +Infinity is not.
        //
        // This matters because lux_value is the ground truth for RQ1: one NaN turns every mean,
        // deviation and correlation CV-12 computes into NaN. No exception, no log — just numbers
        // that are no longer numbers.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_lux_reading_value_non_negative",
            "lux_value >= 0 AND lux_value <> 'NaN'::float8 AND lux_value <> 'Infinity'::float8"));

        // LUX-BASELINE D-4: a protocol is all-or-nothing (firmware id stays optional: the app may not know it), its numbers finite.
        builder.Property(reading => reading.SensorHeightM).HasColumnType("double precision");
        builder.Property(reading => reading.ModuleId).HasColumnType("text");
        builder.Property(reading => reading.OpticalFilterTransmittance).HasColumnType("double precision");
        builder.Property(reading => reading.WindowSpread).HasColumnType("double precision");
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_lux_reading_protocol",
            "(sensor_height_m IS NULL) = (module_id IS NULL) AND (sensor_height_m IS NULL) = (window_spread IS NULL)"
            + " AND (sensor_height_m IS NOT NULL OR (optical_filter_transmittance IS NULL AND module_firmware_version_id IS NULL))"
            + " AND (sensor_height_m IS NULL OR (sensor_height_m > 0 AND sensor_height_m < 'Infinity'::float8))"
            + " AND (window_spread IS NULL OR (window_spread >= 0 AND window_spread < 'Infinity'::float8))"
            + " AND (optical_filter_transmittance IS NULL OR (optical_filter_transmittance > 0 AND optical_filter_transmittance <= 1))"
            + " AND (module_id IS NULL OR length(btrim(module_id)) > 0)"));

        // The real de-duplication guard. A read-then-write check would let two simultaneous retries
        // both pass and both insert; the index cannot.
        builder.HasIndex(reading => reading.ClientOpId)
            .IsUnique()
            .HasDatabaseName("ux_lux_reading_client_op_id");

        // Serves GET /lux-readings/poles/{pole_id}, which reads one pole's series ordered by time.
        builder.HasIndex(reading => new { reading.PoleId, reading.MeasuredAt })
            .HasDatabaseName("ix_lux_reading_pole_id_measured_at");

        // Serves the data_source filter on GET /lux-readings; Branch C keeps calibration data
        // separable from every other source in every statistic.
        builder.HasIndex(reading => reading.DataSource)
            .HasDatabaseName("ix_lux_reading_data_source");

        // RESTRICT, never cascade. A lux reading is an event that happened and the ground truth for
        // RQ1 — deleting a pole must not silently delete research data. It also keeps this out of the
        // cascade blind spot recorded in docs/agents/traps/commune-scope.md, section 1c: the SaveChanges guard cannot see deletions the
        // database performs on its own.
        //
        // Declared WITHOUT a navigation property, the same convention as HasCommuneReference:
        // coupling between modules stays at the level of an id string, so nothing here drags the
        // Assets entity graph into Survey.
        builder.HasOne<Pole>()
            .WithMany()
            .HasForeignKey(reading => reading.PoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // LUX-PHOTO: the photo the reading was taken with. Composite, so the database itself refuses a photo of another
        // pole or commune (same shape as FaultOriginPolePhotoConfiguration); MATCH SIMPLE skips the check when photo_id is
        // null. RESTRICT like every FK here, and photos are only ever withdrawn (soft), never deleted.
        builder.HasOne<WorkOrderPolePhoto>()
            .WithMany()
            .HasForeignKey(reading => new { reading.PhotoId, reading.PoleId, reading.CommuneId })
            .HasPrincipalKey(photo => new { photo.PhotoId, photo.PoleId, photo.CommuneId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lux_reading_pole_photo");

        // At most one reading per photo. Unique on the FK's three columns rather than photo_id alone: photo_id is the
        // photo's primary key and the FK pins the other two to it, so the two are equivalent — and this one index also
        // serves the FK, so EF adds no second one. LuxReadingService maps a violation to 409 PHOTO_LUX_ALREADY_LINKED.
        builder.HasIndex(reading => new { reading.PhotoId, reading.PoleId, reading.CommuneId })
            .IsUnique()
            .HasFilter("photo_id IS NOT NULL")
            .HasDatabaseName("ux_lux_reading_photo_id");

        // Same shape for the person who took the reading. RESTRICT so an account cannot be removed
        // while measurements still point at it: losing who measured what would break the audit trail
        // the task list asks for, and a dangling id is not traceability either.
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(reading => reading.MeasuredBy)
            .OnDelete(DeleteBehavior.Restrict);

        // LUX-BASELINE D-4: the raw window rides on the reading so one SaveChanges writes both; RESTRICT like every FK here.
        builder.HasMany(reading => reading.Samples).WithOne().HasForeignKey(sample => sample.LuxId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LuxReadingSampleConfiguration : IEntityTypeConfiguration<LuxReadingSample>
{
    public void Configure(EntityTypeBuilder<LuxReadingSample> builder)
    {
        // Same NaN / Infinity guard as lux_value: one NaN sample would turn the window's median into nonsense.
        builder.ToTable("lux_reading_sample", table => table.HasCheckConstraint("ck_lux_reading_sample_value",
            "seq >= 0 AND lux >= 0 AND lux <> 'NaN'::float8 AND lux <> 'Infinity'::float8"));
        builder.HasKey(sample => new { sample.LuxId, sample.Seq });
        builder.Property(sample => sample.LuxId).HasColumnType("text");
        builder.Property(sample => sample.Lux).HasColumnType("double precision");
    }
}
