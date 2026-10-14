using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.AddColumn<string>(
                name: "origin_node_id",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scheduled_job",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "text", nullable: false),
                    next_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    run_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job", x => x.job_name);
                    table.CheckConstraint("ck_scheduled_job_last_error_length", "last_error IS NULL OR char_length(last_error) <= 1000");
                    table.CheckConstraint("ck_scheduled_job_lease_together", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_scheduled_job_name", "job_name ~ '^[a-z][a-z0-9_]{1,62}$'");
                    table.CheckConstraint("ck_scheduled_job_run_count", "run_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "work_order_overdue_notice",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_overdue_notice", x => new { x.work_order_id, x.due_date });
                    table.ForeignKey(
                        name: "fk_work_order_overdue_notice_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_overdue_notice_work_order",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");

            migrationBuilder.CreateIndex(
                name: "ix_fault_origin_node_id_commune_id",
                table: "fault",
                columns: new[] { "origin_node_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_fault_origin_node_id_detected_at",
                table: "fault",
                columns: new[] { "origin_node_id", "detected_at" },
                unique: true,
                filter: "fault_type = 'node_offline'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_node_source",
                table: "fault",
                sql: "origin_node_id IS NULL OR (source_channel = 'iot' AND pole_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_overdue_notice_commune_id",
                table: "work_order_overdue_notice",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_overdue_notice_work_order_id_commune_id",
                table: "work_order_overdue_notice",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_fault_iot_node_origin_node_id_commune_id",
                table: "fault",
                columns: new[] { "origin_node_id", "commune_id" },
                principalTable: "iot_node",
                principalColumns: new[] { "node_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_fault_iot_node_origin_node_id_commune_id",
                table: "fault");

            migrationBuilder.DropTable(
                name: "scheduled_job");

            migrationBuilder.DropTable(
                name: "work_order_overdue_notice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropIndex(
                name: "ix_fault_origin_node_id_commune_id",
                table: "fault");

            migrationBuilder.DropIndex(
                name: "ux_fault_origin_node_id_detected_at",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_node_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "origin_node_id",
                table: "fault");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received')");
        }
    }
}
