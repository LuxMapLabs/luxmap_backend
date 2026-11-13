using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPoleGpsUpdateWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order");

            migrationBuilder.CreateSequence(
                name: "work_order_attachment_id_seq");

            migrationBuilder.CreateTable(
                name: "work_order_attachment",
                columns: table => new
                {
                    attachment_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('ATT', nextval('work_order_attachment_id_seq'), 4)"),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_attachment", x => x.attachment_id);
                    table.CheckConstraint("ck_work_order_attachment_bytes", "byte_count > 0");
                    table.CheckConstraint("ck_work_order_attachment_file_name", "btrim(file_name) <> '' AND char_length(file_name) <= 200");
                    table.CheckConstraint("ck_work_order_attachment_key_not_blank", "btrim(object_key) <> ''");
                    table.CheckConstraint("ck_work_order_attachment_removed_after_upload", "removed_at IS NULL OR removed_at >= uploaded_at");
                    table.CheckConstraint("ck_work_order_attachment_removed_together", "(removed_at IS NULL) = (removed_by IS NULL)");
                    table.CheckConstraint("ck_work_order_attachment_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_work_order_attachment_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_attachment_app_user_removed_by",
                        column: x => x.removed_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_attachment_app_user_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_attachment_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_pole_target",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    reported_lat = table.Column<double>(type: "double precision", nullable: true),
                    reported_lng = table.Column<double>(type: "double precision", nullable: true),
                    reported_accuracy_m = table.Column<double>(type: "double precision", nullable: true),
                    reported_captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_pole_target", x => new { x.work_order_id, x.position });
                    table.CheckConstraint("ck_work_order_pole_target_accuracy", "reported_accuracy_m IS NULL OR (reported_accuracy_m >= 0 AND reported_accuracy_m <> 'NaN'::float8 AND reported_accuracy_m <> 'Infinity'::float8)");
                    table.CheckConstraint("ck_work_order_pole_target_lat", "reported_lat IS NULL OR (reported_lat >= -90 AND reported_lat <= 90 AND reported_lat <> 'NaN'::float8)");
                    table.CheckConstraint("ck_work_order_pole_target_lng", "reported_lng IS NULL OR (reported_lng >= -180 AND reported_lng <= 180 AND reported_lng <> 'NaN'::float8)");
                    table.CheckConstraint("ck_work_order_pole_target_position", "position >= 0");
                    table.CheckConstraint("ck_work_order_pole_target_report_complete", "(reported_lat IS NULL AND reported_lng IS NULL AND reported_accuracy_m IS NULL AND reported_captured_at IS NULL)\nOR (reported_lat IS NOT NULL AND reported_lng IS NOT NULL AND reported_accuracy_m IS NOT NULL AND reported_captured_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_work_order_pole_target_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_target_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_target_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_pole_gps_target",
                table: "work_order",
                sql: "task_kind <> 'pole_gps_update' OR (segment_id IS NOT NULL AND cluster_id IS NULL AND parent_work_order_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order",
                sql: "\"task_kind\" IN ('inspection', 'repair', 'survey', 'pole_gps_update')");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_attachment_commune_id",
                table: "work_order_attachment",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_attachment_removed_by",
                table: "work_order_attachment",
                column: "removed_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_attachment_uploaded_by",
                table: "work_order_attachment",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_attachment_work_order_id_commune_id",
                table: "work_order_attachment",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_target_commune_id",
                table: "work_order_pole_target",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_target_pole_id",
                table: "work_order_pole_target",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_target_work_order_id_commune_id",
                table: "work_order_pole_target",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_target_work_order_id_pole_id",
                table: "work_order_pole_target",
                columns: new[] { "work_order_id", "pole_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_work_order_pole_target_pole_id_active",
                table: "work_order_pole_target",
                column: "pole_id",
                unique: true,
                filter: "released_at IS NULL");

            // DB-only, as for pole photos (AddWorkOrderPolePhotos): Pole.CommuneId stays mutable in EF, but a target must stand in its
            // order's commune. Reuses that migration's unique (pole_id, commune_id).
            migrationBuilder.Sql("ALTER TABLE work_order_pole_target ADD CONSTRAINT fk_work_order_pole_target_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old CHECK refuses the new kind, and the audit trail of those orders cannot be deleted to make room (migrations.md).
            migrationBuilder.Sql("""
                DO $$
                DECLARE gps_orders bigint;
                BEGIN
                    SELECT count(*) INTO gps_orders FROM work_order WHERE task_kind = 'pole_gps_update';
                    IF gps_orders > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back AddPoleGpsUpdateWorkOrders: % pole_gps_update work order(s) exist. Roll back the application and keep this schema.', gps_orders;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "work_order_attachment");

            migrationBuilder.DropTable(
                name: "work_order_pole_target");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_pole_gps_target",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order");

            migrationBuilder.DropSequence(
                name: "work_order_attachment_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order",
                sql: "\"task_kind\" IN ('inspection', 'repair', 'survey')");
        }
    }
}
