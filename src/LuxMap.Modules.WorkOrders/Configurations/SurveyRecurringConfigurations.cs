using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.WorkOrders.Configurations;

public sealed class SurveyRecurringScheduleConfiguration : IEntityTypeConfiguration<SurveyRecurringSchedule>
{
    public const string UniqueSegmentDay = "ux_survey_recurring_schedule_segment_day";
    public void Configure(EntityTypeBuilder<SurveyRecurringSchedule> b)
    {
        b.ToTable("survey_recurring_schedule", t =>
        {
            t.HasCheckConstraint("ck_survey_recurring_schedule_day", "day_of_week BETWEEN 1 AND 7");
            t.HasCheckConstraint("ck_survey_recurring_schedule_week", "EXTRACT(ISODOW FROM starts_on_week) = 1");
            t.HasCheckConstraint("ck_survey_recurring_schedule_note", "note IS NULL OR char_length(note) <= 1000");
        });
        b.HasKey(x => x.ScheduleId);
        b.HasAlternateKey(x => new { x.ScheduleId, x.CommuneId });
        b.Property(x => x.ScheduleId).HasPrefixedId(PrefixedIds.SurveyRecurringSchedule);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.SegmentId, x.DayOfWeek }).IsUnique().HasDatabaseName(UniqueSegmentDay);
        b.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AssignedTo).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SurveyRecurringOccurrenceConfiguration : IEntityTypeConfiguration<SurveyRecurringOccurrence>
{
    public const string PrimaryKey = "pk_survey_recurring_occurrence";
    public void Configure(EntityTypeBuilder<SurveyRecurringOccurrence> b)
    {
        b.ToTable("survey_recurring_occurrence");
        b.HasKey(x => new { x.ScheduleId, x.ScheduledDate }).HasName(PrimaryKey);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasIndex(x => x.CommuneId);
        // Composite FK enforces SAME commune at the DB boundary: cascade cannot cross the write guard's scope.
        b.HasOne<SurveyRecurringSchedule>().WithMany().HasForeignKey(x => new { x.ScheduleId, x.CommuneId })
            .HasPrincipalKey(x => new { x.ScheduleId, x.CommuneId }).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_survey_recurring_occurrence_schedule");
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => new { x.WorkOrderId, x.CommuneId })
            .HasPrincipalKey(x => new { x.WorkOrderId, x.CommuneId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_survey_recurring_occurrence_work_order");
    }
}
