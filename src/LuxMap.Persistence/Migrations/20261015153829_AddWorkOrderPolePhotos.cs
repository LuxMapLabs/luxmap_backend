using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderPolePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "pole_photo_id_seq");

            migrationBuilder.AddColumn<string>(
                name: "origin_pole_photo_id",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_order_pole_photo",
                columns: table => new
                {
                    photo_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('PPH', nextval('pole_photo_id_seq'), 6)"),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    cv_state = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    model_version = table.Column<string>(type: "text", nullable: true),
                    artifact_version = table.Column<string>(type: "text", nullable: true),
                    analyzed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    status_applied = table.Column<bool>(type: "boolean", nullable: false),
                    status_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_pole_photo", x => x.photo_id);
                    table.UniqueConstraint("ak_work_order_pole_photo_photo_id_pole_id_commune_id", x => new { x.photo_id, x.pole_id, x.commune_id });
                    table.CheckConstraint("ck_pole_photo_ai", "(cv_state IS NULL AND confidence IS NULL AND model_version IS NULL AND artifact_version IS NULL AND analyzed_at IS NULL) OR (cv_state IS NOT NULL AND cv_state IN ('on','off','inconclusive') AND model_version IS NOT NULL AND btrim(model_version) <> '' AND artifact_version IS NOT NULL AND btrim(artifact_version) <> '' AND analyzed_at IS NOT NULL AND ((cv_state = 'inconclusive' AND confidence IS NULL) OR (cv_state IN ('on','off') AND confidence IS NOT NULL AND confidence BETWEEN 0 AND 1 AND confidence <> 'NaN'::float8)))");
                    table.CheckConstraint("ck_pole_photo_decision", "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (cv_state IS NOT NULL AND cv_state IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
                    table.CheckConstraint("ck_pole_photo_keys", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> '' AND request_hash ~ '^[0-9a-f]{64}$' AND client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_pole_photo_lat", "lat BETWEEN -90 AND 90 AND lat <> 'NaN'::float8");
                    table.CheckConstraint("ck_pole_photo_lng", "lng BETWEEN -180 AND 180 AND lng <> 'NaN'::float8");
                    table.CheckConstraint("ck_pole_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
                    table.CheckConstraint("ck_work_order_pole_photo_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.ForeignKey(
                        name: "fk_work_order_pole_photo_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_photo_app_user_decided_by",
                        column: x => x.decided_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_photo_app_user_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_photo_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_pole_photo_work_order_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_order",
                        principalColumn: "work_order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fault_origin_pole_photo_id_pole_id_commune_id",
                table: "fault",
                columns: new[] { "origin_pole_photo_id", "pole_id", "commune_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault",
                sql: "origin_pole_photo_id IS NULL OR (source_channel = 'cv' AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_photo_commune_id",
                table: "work_order_pole_photo",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_photo_decided_by",
                table: "work_order_pole_photo",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_photo_pole_id_captured_at",
                table: "work_order_pole_photo",
                columns: new[] { "pole_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_pole_photo_work_order_id_captured_at_uploaded_at",
                table: "work_order_pole_photo",
                columns: new[] { "work_order_id", "captured_at", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pole_photo_user_operation",
                table: "work_order_pole_photo",
                columns: new[] { "uploaded_by", "client_op_id" },
                unique: true);

            // DB-only: Pole.CommuneId stays mutable in EF; the photo must still belong to its pole's commune.
            migrationBuilder.Sql("ALTER TABLE pole ADD CONSTRAINT ak_pole_photo_target UNIQUE (pole_id, commune_id)");
            migrationBuilder.Sql("ALTER TABLE work_order_pole_photo ADD CONSTRAINT fk_pole_photo_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT");

            migrationBuilder.AddForeignKey(
                name: "fk_fault_work_order_pole_photo_origin_pole_photo_id_pole_id_co",
                table: "fault",
                columns: new[] { "origin_pole_photo_id", "pole_id", "commune_id" },
                principalTable: "work_order_pole_photo",
                principalColumns: new[] { "photo_id", "pole_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_fault_work_order_pole_photo_origin_pole_photo_id_pole_id_co",
                table: "fault");

            migrationBuilder.DropTable(
                name: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_fault_origin_pole_photo_id_pole_id_commune_id",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "origin_pole_photo_id",
                table: "fault");

            migrationBuilder.DropSequence(
                name: "pole_photo_id_seq");

            migrationBuilder.Sql("ALTER TABLE pole DROP CONSTRAINT ak_pole_photo_target");
        }
    }
}
