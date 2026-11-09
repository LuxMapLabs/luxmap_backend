using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SurveyRecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "survey_recurring_schedule_id_seq");

            migrationBuilder.CreateTable(
                name: "survey_recurring_schedule",
                columns: table => new
                {
                    schedule_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('SSCH', nextval('survey_recurring_schedule_id_seq'), 4)"),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    assigned_to = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    starts_on_week = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_recurring_schedule", x => x.schedule_id);
                    table.UniqueConstraint("ak_survey_recurring_schedule_schedule_id_commune_id", x => new { x.schedule_id, x.commune_id });
                    table.CheckConstraint("ck_survey_recurring_schedule_day", "day_of_week BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_survey_recurring_schedule_note", "note IS NULL OR char_length(note) <= 1000");
                    table.CheckConstraint("ck_survey_recurring_schedule_week", "EXTRACT(ISODOW FROM starts_on_week) = 1");
                    table.ForeignKey(
                        name: "fk_survey_recurring_schedule_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_recurring_schedule_app_user_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_recurring_schedule_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_recurring_schedule_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_recurring_occurrence",
                columns: table => new
                {
                    schedule_id = table.Column<string>(type: "text", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_recurring_occurrence", x => new { x.schedule_id, x.scheduled_date });
                    table.ForeignKey(
                        name: "fk_survey_recurring_occurrence_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_recurring_occurrence_schedule",
                        columns: x => new { x.schedule_id, x.commune_id },
                        principalTable: "survey_recurring_schedule",
                        principalColumns: new[] { "schedule_id", "commune_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_survey_recurring_occurrence_work_order",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated', 'viewed', 'deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole', 'survey_recurring_schedule')");

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_occurrence_commune_id",
                table: "survey_recurring_occurrence",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_occurrence_schedule_id_commune_id",
                table: "survey_recurring_occurrence",
                columns: new[] { "schedule_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_occurrence_work_order_id_commune_id",
                table: "survey_recurring_occurrence",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_schedule_assigned_to",
                table: "survey_recurring_schedule",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_schedule_commune_id",
                table: "survey_recurring_schedule",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_recurring_schedule_created_by",
                table: "survey_recurring_schedule",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_survey_recurring_schedule_segment_day",
                table: "survey_recurring_schedule",
                columns: new[] { "segment_id", "day_of_week" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // audit_event is append-only: rows that name the new entity type or action cannot be purged to let the old CHECKs back in.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM audit_event WHERE entity_type = 'survey_recurring_schedule' OR action = 'deleted') THEN
                        RAISE EXCEPTION 'Cannot roll back SurveyRecurringSchedules while audit events for recurring survey schedules exist';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "survey_recurring_occurrence");

            migrationBuilder.DropTable(
                name: "survey_recurring_schedule");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "survey_recurring_schedule_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated', 'viewed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole')");
        }
    }
}
