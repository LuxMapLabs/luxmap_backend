using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_entity_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "citizen_photo_id_seq");

            migrationBuilder.CreateSequence(
                name: "citizen_report_id_seq");

            migrationBuilder.CreateSequence(
                name: "citizen_submission_id_seq");

            migrationBuilder.CreateTable(
                name: "citizen_report",
                columns: table => new
                {
                    report_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CRP', nextval('citizen_report_id_seq'), 4)"),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    night_of = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fault_id = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_citizen_report", x => x.report_id);
                    table.CheckConstraint("ck_citizen_report_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_citizen_report_decision_columns", "CASE status WHEN 'pending' THEN fault_id IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL WHEN 'rejected' THEN fault_id IS NULL AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND decision_note IS NOT NULL ELSE fault_id IS NOT NULL AND decided_by IS NOT NULL AND decided_at IS NOT NULL END");
                    table.CheckConstraint("ck_citizen_report_decision_note_length", "decision_note IS NULL OR char_length(decision_note) BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_citizen_report_status", "\"status\" IN ('pending', 'accepted', 'merged', 'rejected')");
                    table.CheckConstraint("ck_citizen_report_times_ordered", "last_submitted_at >= created_at AND (decided_at IS NULL OR decided_at >= created_at)");
                    table.ForeignKey(
                        name: "fk_citizen_report_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_report_app_user_decided_by",
                        column: x => x.decided_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_report_fault_fault_id_commune_id",
                        columns: x => new { x.fault_id, x.commune_id },
                        principalTable: "fault",
                        principalColumns: new[] { "fault_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_report_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "citizen_submission",
                columns: table => new
                {
                    submission_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CSB', nextval('citizen_submission_id_seq'), 6)"),
                    report_id = table.Column<string>(type: "text", nullable: false),
                    qr_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    reporter_lat = table.Column<double>(type: "double precision", nullable: true),
                    reporter_lng = table.Column<double>(type: "double precision", nullable: true),
                    far_from_pole = table.Column<bool>(type: "boolean", nullable: false),
                    photos_dropped = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_citizen_submission", x => x.submission_id);
                    table.CheckConstraint("ck_citizen_submission_description_length", "description IS NULL OR char_length(description) BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_citizen_submission_far_needs_position", "NOT far_from_pole OR reporter_lat IS NOT NULL");
                    table.CheckConstraint("ck_citizen_submission_kind", "\"kind\" IN ('lamp_out', 'lamp_dim', 'pole_damaged', 'other')");
                    table.CheckConstraint("ck_citizen_submission_photos_dropped", "\"photos_dropped\" IS NULL OR \"photos_dropped\" IN ('over_quota', 'over_budget')");
                    table.CheckConstraint("ck_citizen_submission_position", "(reporter_lat IS NULL) = (reporter_lng IS NULL) AND (reporter_lat IS NULL OR (reporter_lat >= -90 AND reporter_lat <= 90 AND reporter_lng >= -180 AND reporter_lng <= 180))");
                    table.CheckConstraint("ck_citizen_submission_request_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_citizen_submission_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_submission_citizen_report_report_id",
                        column: x => x.report_id,
                        principalTable: "citizen_report",
                        principalColumn: "report_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_submission_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_citizen_submission_qr_code_qr_id",
                        column: x => x.qr_id,
                        principalTable: "qr_code",
                        principalColumn: "qr_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "citizen_photo",
                columns: table => new
                {
                    photo_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CPH', nextval('citizen_photo_id_seq'), 6)"),
                    submission_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_citizen_photo", x => x.photo_id);
                    table.CheckConstraint("ck_citizen_photo_bytes", "byte_count > 0 AND thumbnail_bytes > 0");
                    table.CheckConstraint("ck_citizen_photo_keys_not_blank", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> ''");
                    table.CheckConstraint("ck_citizen_photo_position", "position BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_citizen_photo_citizen_submission_submission_id",
                        column: x => x.submission_id,
                        principalTable: "citizen_submission",
                        principalColumn: "submission_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_entity_type",
                table: "notification",
                sql: "\"entity_type\" IN ('work_order', 'survey_sweep', 'fault', 'citizen_report')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event",
                sql: "\"actor_kind\" IN ('user', 'cv', 'iot', 'system', 'public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report')");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_photo_received_at",
                table: "citizen_photo",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_photo_submission_id_position",
                table: "citizen_photo",
                columns: new[] { "submission_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_citizen_report_commune_id",
                table: "citizen_report",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_report_decided_by",
                table: "citizen_report",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_report_fault_id_commune_id",
                table: "citizen_report",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_citizen_report_pole_id",
                table: "citizen_report",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ux_citizen_report_pole_id_night_of_pending",
                table: "citizen_report",
                columns: new[] { "pole_id", "night_of" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_submission_commune_id",
                table: "citizen_submission",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_submission_pole_id",
                table: "citizen_submission",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ix_citizen_submission_qr_id_received_at",
                table: "citizen_submission",
                columns: new[] { "qr_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_citizen_submission_report_id",
                table: "citizen_submission",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ux_citizen_submission_client_op_id",
                table: "citizen_submission",
                column: "client_op_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "citizen_photo");

            migrationBuilder.DropTable(
                name: "citizen_submission");

            migrationBuilder.DropTable(
                name: "citizen_report");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_entity_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "citizen_photo_id_seq");

            migrationBuilder.DropSequence(
                name: "citizen_report_id_seq");

            migrationBuilder.DropSequence(
                name: "citizen_submission_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_entity_type",
                table: "notification",
                sql: "\"entity_type\" IN ('work_order', 'survey_sweep', 'fault')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event",
                sql: "\"actor_kind\" IN ('user', 'cv', 'iot', 'system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code')");
        }
    }
}
