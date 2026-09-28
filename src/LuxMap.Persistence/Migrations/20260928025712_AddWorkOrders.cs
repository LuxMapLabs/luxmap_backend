using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "work_order_id_seq");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateTable(
                name: "work_order",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('WO', nextval('work_order_id_seq'), 4)"),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    task_kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    wo_status = table.Column<string>(type: "text", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: true),
                    cluster_id = table.Column<string>(type: "text", nullable: true),
                    assigned_to = table.Column<string>(type: "text", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    report_note = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order", x => x.work_order_id);
                    table.UniqueConstraint("ak_work_order_work_order_id_commune_id", x => new { x.work_order_id, x.commune_id });
                    table.CheckConstraint("ck_work_order_assigned_at_matches", "(assigned_to IS NULL) = (assigned_at IS NULL)");
                    table.CheckConstraint("ck_work_order_assignee_matches_status", "(wo_status = 'open' AND assigned_to IS NULL) OR (wo_status IN ('assigned','in_progress','done','verified') AND assigned_to IS NOT NULL) OR wo_status = 'cancelled'");
                    table.CheckConstraint("ck_work_order_closed", "(wo_status IN ('verified','cancelled')) = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_completed", "wo_status NOT IN ('done','verified') OR (completed_at IS NOT NULL AND report_note IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_schedule_before_due", "scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date");
                    table.CheckConstraint("ck_work_order_started", "wo_status NOT IN ('in_progress','done','verified') OR started_at IS NOT NULL");
                    table.CheckConstraint("ck_work_order_task_kind", "\"task_kind\" IN ('inspection', 'repair')");
                    table.CheckConstraint("ck_work_order_title_not_blank", "btrim(title) <> ''");
                    table.CheckConstraint("ck_work_order_wo_status", "\"wo_status\" IN ('open', 'assigned', 'in_progress', 'done', 'verified', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_work_order_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_cluster_cluster_id",
                        column: x => x.cluster_id,
                        principalTable: "fault_cluster",
                        principalColumn: "cluster_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_fault",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    fault_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspection_outcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_fault", x => new { x.work_order_id, x.fault_id });
                    table.CheckConstraint("ck_work_order_fault_inspection_outcome", "\"inspection_outcome\" IS NULL OR \"inspection_outcome\" IN ('fault_present', 'fault_absent', 'inconclusive')");
                    table.CheckConstraint("ck_work_order_fault_release_after_link", "released_at IS NULL OR released_at >= linked_at");
                    table.ForeignKey(
                        name: "fk_work_order_fault_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_fault_fault_id_commune_id",
                        columns: x => new { x.fault_id, x.commune_id },
                        principalTable: "fault",
                        principalColumns: new[] { "fault_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_assigned_to",
                table: "work_order",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_cluster_id",
                table: "work_order",
                column: "cluster_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_commune_id",
                table: "work_order",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_created_by",
                table: "work_order",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_id",
                table: "work_order",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_wo_status",
                table: "work_order",
                column: "wo_status");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_commune_id",
                table: "work_order_fault",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id",
                table: "work_order_fault",
                column: "fault_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_work_order_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_work_order_fault_fault_id_active",
                table: "work_order_fault",
                column: "fault_id",
                unique: true,
                filter: "released_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_fault");

            migrationBuilder.DropTable(
                name: "work_order");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault");

            migrationBuilder.DropSequence(
                name: "work_order_id_seq");
        }
    }
}
