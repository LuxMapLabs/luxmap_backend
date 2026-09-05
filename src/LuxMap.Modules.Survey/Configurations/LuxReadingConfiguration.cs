using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
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
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_lux_reading_value_non_negative", "lux_value >= 0"));

        // The real de-duplication guard. A read-then-write check would let two simultaneous retries
        // both pass and both insert; the index cannot.
        builder.HasIndex(reading => reading.ClientOpId)
            .IsUnique()
            .HasDatabaseName("ux_lux_reading_client_op_id");

        // Serves GET /poles/{pole_id}/lux-readings, which reads one pole's series ordered by time.
        builder.HasIndex(reading => new { reading.PoleId, reading.MeasuredAt })
            .HasDatabaseName("ix_lux_reading_pole_id_measured_at");

        // Serves the data_source filter on GET /lux-readings; Branch C keeps calibration data
        // separable from every other source in every statistic.
        builder.HasIndex(reading => reading.DataSource)
            .HasDatabaseName("ix_lux_reading_data_source");

        // RESTRICT, never cascade. A lux reading is an event that happened and the ground truth for
        // RQ1 — deleting a pole must not silently delete research data. It also keeps this out of the
        // cascade blind spot recorded in CLAUDE.md 1c: the SaveChanges guard cannot see deletions the
        // database performs on its own.
        //
        // Declared WITHOUT a navigation property, the same convention as HasCommuneReference:
        // coupling between modules stays at the level of an id string, so nothing here drags the
        // Assets entity graph into Survey.
        builder.HasOne<Pole>()
            .WithMany()
            .HasForeignKey(reading => reading.PoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same shape for the person who took the reading. RESTRICT so an account cannot be removed
        // while measurements still point at it: losing who measured what would break the audit trail
        // the task list asks for, and a dangling id is not traceability either.
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(reading => reading.MeasuredBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
