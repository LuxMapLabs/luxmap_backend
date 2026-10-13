using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Citizen.Configurations;

public sealed class CitizenReportConfiguration : IEntityTypeConfiguration<CitizenReport>
{
    public const string PendingPerPoleNightIndex = "ux_citizen_report_pole_id_night_of_pending";

    public void Configure(EntityTypeBuilder<CitizenReport> builder)
    {
        builder.ToTable("citizen_report", table =>
        {
            // Each status says which decision columns it carries. IS NOT NULL is spelled out: a CHECK lets NULL through.
            table.HasCheckConstraint("ck_citizen_report_decision_columns",
                "CASE status"
                + " WHEN 'pending' THEN fault_id IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL"
                + " WHEN 'rejected' THEN fault_id IS NULL AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND decision_note IS NOT NULL"
                + " ELSE fault_id IS NOT NULL AND decided_by IS NOT NULL AND decided_at IS NOT NULL"
                + " END");
            table.HasCheckConstraint("ck_citizen_report_times_ordered",
                "last_submitted_at >= created_at AND (decided_at IS NULL OR decided_at >= created_at)");
            table.HasCheckConstraint("ck_citizen_report_decision_note_length",
                "decision_note IS NULL OR char_length(decision_note) BETWEEN 1 AND 1000");
        });

        builder.HasKey(report => report.ReportId);
        builder.Property(report => report.ReportId).HasPrefixedId(PrefixedIds.CitizenReport);
        builder.Property(report => report.PoleId).HasColumnType("text").IsRequired();
        builder.Property(report => report.FaultId).HasColumnType("text");
        builder.Property(report => report.DecidedBy).HasColumnType("text");
        builder.Property(report => report.DecisionNote).HasColumnType("text");
        builder.HasContractEnum(report => report.DataSource);
        builder.HasContractEnum(report => report.Status);

        builder.HasIndex(report => new { report.PoleId, report.NightOf }, PendingPerPoleNightIndex)
            .IsUnique().HasFilter("status = 'pending'").HasDatabaseName(PendingPerPoleNightIndex);
        // The partial index above leads with pole_id; the FK index must be declared or EF drops it (partial-index rule).
        builder.HasIndex(report => report.PoleId, "ix_citizen_report_pole_id").HasDatabaseName("ix_citizen_report_pole_id");
        builder.HasIndex(report => report.CommuneId);
        builder.HasIndex(report => new { report.FaultId, report.CommuneId });
        builder.HasIndex(report => report.DecidedBy);

        builder.HasOne<Pole>().WithMany().HasForeignKey(report => report.PoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(report => report.DecidedBy).OnDelete(DeleteBehavior.Restrict);
        // Composite with commune_id (Fault's alternate key): a report can only ever point at a fault of its own commune. MATCH SIMPLE
        // skips the check while fault_id is null — exactly the pending / rejected rows.
        builder.HasOne<Fault>().WithMany()
            .HasForeignKey(report => new { report.FaultId, report.CommuneId })
            .HasPrincipalKey(fault => new { fault.FaultId, fault.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        // The target of citizen_submission's composite key: a send joins only an item of its own pole and commune (review P2b #8).
        builder.HasAlternateKey(report => new { report.ReportId, report.PoleId, report.CommuneId });

        builder.HasCommuneScope();
        builder.HasCommuneReference(report => report.CommuneId);
    }
}

public sealed class CitizenSubmissionConfiguration : IEntityTypeConfiguration<CitizenSubmission>
{
    public const string ClientOpIdIndex = "ux_citizen_submission_client_op_id";

    public void Configure(EntityTypeBuilder<CitizenSubmission> builder)
    {
        builder.ToTable("citizen_submission", table =>
        {
            table.HasCheckConstraint("ck_citizen_submission_description_length",
                "description IS NULL OR char_length(description) BETWEEN 1 AND 1000");
            // Ranges exclude NaN and ±Infinity too: Postgres sorts NaN above every number, so 'NaN' <= 90 is false.
            table.HasCheckConstraint("ck_citizen_submission_position",
                "(reporter_lat IS NULL) = (reporter_lng IS NULL)"
                + " AND (reporter_lat IS NULL OR (reporter_lat >= -90 AND reporter_lat <= 90 AND reporter_lng >= -180 AND reporter_lng <= 180))");
            table.HasCheckConstraint("ck_citizen_submission_far_needs_position", "NOT far_from_pole OR reporter_lat IS NOT NULL");
            table.HasCheckConstraint("ck_citizen_submission_request_hash", "request_hash ~ '^[0-9a-f]{64}$'");
        });

        builder.HasKey(submission => submission.SubmissionId);
        builder.Property(submission => submission.SubmissionId).HasPrefixedId(PrefixedIds.CitizenSubmission);
        builder.Property(submission => submission.ReportId).HasColumnType("text").IsRequired();
        builder.Property(submission => submission.QrId).HasColumnType("text").IsRequired();
        builder.Property(submission => submission.PoleId).HasColumnType("text").IsRequired();
        builder.Property(submission => submission.RequestHash).HasColumnType("text").IsRequired();
        builder.Property(submission => submission.Description).HasColumnType("text");
        builder.HasContractEnum(submission => submission.Kind);
        builder.HasContractEnum(submission => submission.PhotosDropped);

        builder.HasIndex(submission => submission.ClientOpId).IsUnique().HasDatabaseName(ClientOpIdIndex);
        builder.HasIndex(submission => new { submission.ReportId, submission.PoleId, submission.CommuneId });
        builder.HasIndex(submission => new { submission.QrId, submission.PoleId, submission.CommuneId });
        builder.HasIndex(submission => new { submission.QrId, submission.ReceivedAt });
        builder.HasIndex(submission => submission.PoleId);
        builder.HasIndex(submission => submission.CommuneId);

        // Composite (review P2b #8): the report and the code of a send are on the SAME pole and commune as the send — a write in one
        // commune's scope cannot attach to a parent of another, which the commune guard alone would not see (it checks this row only).
        // report → pole stays a single key: pole has no (pole_id, commune_id) key, and the server copies the commune from the code.
        builder.HasOne<CitizenReport>().WithMany()
            .HasForeignKey(submission => new { submission.ReportId, submission.PoleId, submission.CommuneId })
            .HasPrincipalKey(report => new { report.ReportId, report.PoleId, report.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QrCode>().WithMany()
            .HasForeignKey(submission => new { submission.QrId, submission.PoleId, submission.CommuneId })
            .HasPrincipalKey(qr => new { qr.QrId, qr.PoleId, qr.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Pole>().WithMany().HasForeignKey(submission => submission.PoleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasCommuneScope();
        builder.HasCommuneReference(submission => submission.CommuneId);
    }
}

public sealed class CitizenPhotoConfiguration : IEntityTypeConfiguration<CitizenPhoto>
{
    public void Configure(EntityTypeBuilder<CitizenPhoto> builder)
    {
        builder.ToTable("citizen_photo", table =>
        {
            table.HasCheckConstraint("ck_citizen_photo_position", "position BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_citizen_photo_bytes", "byte_count > 0 AND thumbnail_bytes > 0");
            table.HasCheckConstraint("ck_citizen_photo_keys_not_blank", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> ''");
            // P2c (D-9): the hint, its version and its time come together; a confidence only behind a lamp_on / lamp_off hint,
            // finite and within 0..1 (Postgres reads 'NaN' >= 0 as true — the double precision rule).
            table.HasCheckConstraint("ck_citizen_photo_ai_together",
                "(ai_hint IS NULL) = (ai_artifact_version IS NULL) AND (ai_hint IS NULL) = (ai_checked_at IS NULL)");
            table.HasCheckConstraint("ck_citizen_photo_ai_confidence",
                "(ai_confidence IS NULL) = (ai_hint IS NULL OR ai_hint = 'inconclusive') AND (ai_confidence IS NULL OR "
                + "(ai_confidence >= 0 AND ai_confidence <= 1 AND ai_confidence <> 'NaN'::float8))");
            table.HasCheckConstraint("ck_citizen_photo_ai_version_not_blank",
                "ai_artifact_version IS NULL OR btrim(ai_artifact_version) <> ''");
        });

        builder.HasKey(photo => photo.PhotoId);
        builder.Property(photo => photo.PhotoId).HasPrefixedId(PrefixedIds.CitizenPhoto);
        builder.Property(photo => photo.SubmissionId).HasColumnType("text").IsRequired();
        builder.Property(photo => photo.ObjectKey).HasColumnType("text").IsRequired();
        builder.Property(photo => photo.ThumbnailKey).HasColumnType("text").IsRequired();
        builder.HasContractEnum(photo => photo.AiHint);
        builder.Property(photo => photo.AiArtifactVersion).HasColumnType("text");

        builder.HasIndex(photo => new { photo.SubmissionId, photo.Position }).IsUnique();
        // The day's storage budget sums photo bytes by arrival time.
        builder.HasIndex(photo => photo.ReceivedAt);

        builder.HasOne<CitizenSubmission>().WithMany().HasForeignKey(photo => photo.SubmissionId).OnDelete(DeleteBehavior.Restrict);
    }
}
