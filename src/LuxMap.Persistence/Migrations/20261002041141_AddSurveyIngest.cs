using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "sweep_id_seq");

            migrationBuilder.CreateTable(
                name: "survey_sweep",
                columns: table => new
                {
                    sweep_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('SWP', nextval('sweep_id_seq'), 3)"),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    captured_by = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boot_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elapsed_anchor_ns = table.Column<long>(type: "bigint", nullable: false),
                    started_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    ended_elapsed_ns = table.Column<long>(type: "bigint", nullable: true),
                    submission_request_hash = table.Column<string>(type: "text", nullable: true),
                    utc_anchor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    utc_uncertainty_ms = table.Column<double>(type: "double precision", nullable: false),
                    create_request_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    processing_status = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_sweep", x => x.sweep_id);
                    table.CheckConstraint("ck_survey_sweep_clock", "elapsed_anchor_ns >= 0 AND utc_uncertainty_ms >= 0 AND utc_uncertainty_ms < 'Infinity'::float8");
                    table.CheckConstraint("ck_survey_sweep_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_survey_sweep_interval", "started_elapsed_ns >= 0 AND (ended_elapsed_ns IS NULL OR ended_elapsed_ns >= started_elapsed_ns)");
                    table.CheckConstraint("ck_survey_sweep_processing_status", "\"processing_status\" IN ('not_started', 'queued', 'processing', 'succeeded', 'failed')");
                    table.CheckConstraint("ck_survey_sweep_request_hash", "create_request_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_survey_sweep_status", "\"status\" IN ('uploading', 'queued', 'processing', 'awaiting_review', 'accepted', 'returned', 'failed')");
                    table.ForeignKey(
                        name: "fk_survey_sweep_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_sweep_app_user_captured_by",
                        column: x => x.captured_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_sweep_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_segment",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_segment", x => new { x.work_order_id, x.position });
                    table.CheckConstraint("ck_work_order_segment_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_work_order_segment_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_segment_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_segment_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_gps_sample",
                columns: table => new
                {
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    sample_no = table.Column<int>(type: "integer", nullable: false),
                    phone_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    accuracy_m = table.Column<double>(type: "double precision", nullable: false),
                    heading_deg = table.Column<double>(type: "double precision", nullable: true),
                    speed_mps = table.Column<double>(type: "double precision", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_gps_sample", x => new { x.sweep_id, x.sample_no });
                    table.CheckConstraint("ck_survey_gps_sample_accuracy", "accuracy_m >= 0 AND accuracy_m < 'Infinity'::float8");
                    table.CheckConstraint("ck_survey_gps_sample_geom", "NOT ST_IsEmpty(geom) AND ST_X(geom) BETWEEN -180 AND 180 AND ST_Y(geom) BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_survey_gps_sample_heading", "heading_deg IS NULL OR (heading_deg >= 0 AND heading_deg < 360)");
                    table.CheckConstraint("ck_survey_gps_sample_speed", "speed_mps IS NULL OR (speed_mps >= 0 AND speed_mps < 'Infinity'::float8)");
                    table.CheckConstraint("ck_survey_gps_sample_time", "sample_no >= 0 AND phone_elapsed_ns >= 0");
                    table.ForeignKey(
                        name: "fk_survey_gps_sample_survey_sweep_sweep_id",
                        column: x => x.sweep_id,
                        principalTable: "survey_sweep",
                        principalColumn: "sweep_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_lux_sample",
                columns: table => new
                {
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    sample_no = table.Column<int>(type: "integer", nullable: false),
                    module_epoch = table.Column<int>(type: "integer", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    module_ms = table.Column<long>(type: "bigint", nullable: false),
                    phone_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    lux = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_lux_sample", x => new { x.sweep_id, x.sample_no });
                    table.CheckConstraint("ck_survey_lux_sample_lux", "lux >= 0 AND lux < 'Infinity'::float8");
                    table.CheckConstraint("ck_survey_lux_sample_time", "sample_no >= 0 AND phone_elapsed_ns >= 0 AND module_ms >= 0 AND module_epoch >= 0 AND seq >= 0");
                    table.ForeignKey(
                        name: "fk_survey_lux_sample_survey_sweep_sweep_id",
                        column: x => x.sweep_id,
                        principalTable: "survey_sweep",
                        principalColumn: "sweep_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_raw_file",
                columns: table => new
                {
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_raw_file", x => new { x.sweep_id, x.kind });
                    table.CheckConstraint("ck_survey_raw_file_bytes", "byte_count > 0");
                    table.CheckConstraint("ck_survey_raw_file_hash", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_survey_raw_file_kind", "\"kind\" IN ('gps_track', 'lux_log', 'capture_config')");
                    table.CheckConstraint("ck_survey_raw_file_schema", "schema_version = 1");
                    table.ForeignKey(
                        name: "fk_survey_raw_file_survey_sweep_sweep_id",
                        column: x => x.sweep_id,
                        principalTable: "survey_sweep",
                        principalColumn: "sweep_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_video_clip",
                columns: table => new
                {
                    clip_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    clip_no = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    stored_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_video_clip", x => x.clip_id);
                    table.CheckConstraint("ck_survey_video_clip_bytes", "byte_count > 0 AND byte_count <= 314572800");
                    table.CheckConstraint("ck_survey_video_clip_hash", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_survey_video_clip_number", "clip_no >= 0");
                    table.CheckConstraint("ck_survey_video_clip_type", "content_type = 'video/mp4'");
                    table.ForeignKey(
                        name: "fk_survey_video_clip_survey_sweep_sweep_id",
                        column: x => x.sweep_id,
                        principalTable: "survey_sweep",
                        principalColumn: "sweep_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_survey_target",
                table: "work_order",
                sql: "task_kind <> 'survey' OR (segment_id IS NULL AND cluster_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order",
                sql: "\"task_kind\" IN ('inspection', 'repair', 'survey')");

            migrationBuilder.CreateIndex(
                name: "ix_pole_current_status_last_sweep_id",
                table: "pole_current_status",
                column: "last_sweep_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep')");

            migrationBuilder.CreateIndex(
                name: "ix_survey_gps_sample_geom",
                table: "survey_gps_sample",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_survey_gps_sample_sweep_id_phone_elapsed_ns",
                table: "survey_gps_sample",
                columns: new[] { "sweep_id", "phone_elapsed_ns" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_lux_sample_sweep_id_module_epoch_seq",
                table: "survey_lux_sample",
                columns: new[] { "sweep_id", "module_epoch", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_lux_sample_sweep_id_phone_elapsed_ns",
                table: "survey_lux_sample",
                columns: new[] { "sweep_id", "phone_elapsed_ns" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_captured_by_client_op_id",
                table: "survey_sweep",
                columns: new[] { "captured_by", "client_op_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_commune_id",
                table: "survey_sweep",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_work_order_id_commune_id",
                table: "survey_sweep",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_work_order_id_created_at",
                table: "survey_sweep",
                columns: new[] { "work_order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_video_clip_sweep_id_clip_no",
                table: "survey_video_clip",
                columns: new[] { "sweep_id", "clip_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_commune_id",
                table: "work_order_segment",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_segment_id",
                table: "work_order_segment",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_work_order_id_commune_id",
                table: "work_order_segment",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_work_order_id_segment_id",
                table: "work_order_segment",
                columns: new[] { "work_order_id", "segment_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_current_status_survey_sweep_last_sweep_id",
                table: "pole_current_status",
                column: "last_sweep_id",
                principalTable: "survey_sweep",
                principalColumn: "sweep_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pole_current_status_survey_sweep_last_sweep_id",
                table: "pole_current_status");

            migrationBuilder.DropTable(
                name: "survey_gps_sample");

            migrationBuilder.DropTable(
                name: "survey_lux_sample");

            migrationBuilder.DropTable(
                name: "survey_raw_file");

            migrationBuilder.DropTable(
                name: "survey_video_clip");

            migrationBuilder.DropTable(
                name: "work_order_segment");

            migrationBuilder.DropTable(
                name: "survey_sweep");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_survey_target",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order");

            migrationBuilder.DropIndex(
                name: "ix_pole_current_status_last_sweep_id",
                table: "pole_current_status");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "sweep_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_task_kind",
                table: "work_order",
                sql: "\"task_kind\" IN ('inspection', 'repair')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault')");
        }
    }
}
