using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStationaryLuxBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault");

            migrationBuilder.AddColumn<long>(
                name: "module_firmware_version_id",
                table: "lux_reading",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module_id",
                table: "lux_reading",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "optical_filter_transmittance",
                table: "lux_reading",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "sensor_height_m",
                table: "lux_reading",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "window_spread",
                table: "lux_reading",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lux_reading_sample",
                columns: table => new
                {
                    lux_id = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    lux = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lux_reading_sample", x => new { x.lux_id, x.seq });
                    table.CheckConstraint("ck_lux_reading_sample_value", "seq >= 0 AND lux >= 0 AND lux <> 'NaN'::float8 AND lux <> 'Infinity'::float8");
                    table.ForeignKey(
                        name: "fk_lux_reading_sample_lux_reading_lux_id",
                        column: x => x.lux_id,
                        principalTable: "lux_reading",
                        principalColumn: "lux_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stationary_baseline",
                columns: table => new
                {
                    baseline_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    fixture_id = table.Column<string>(type: "text", nullable: true),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    protocol_fingerprint = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    member_cv = table.Column<double>(type: "double precision", nullable: false),
                    algorithm_version_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stationary_baseline", x => x.baseline_id);
                    table.UniqueConstraint("ak_stationary_baseline_baseline_id_pole_id_commune_id", x => new { x.baseline_id, x.pole_id, x.commune_id });
                    table.CheckConstraint("ck_stationary_baseline_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_stationary_baseline_value", "value > 0 AND value < 'Infinity'::float8 AND version > 0 AND member_count > 0 AND member_cv >= 0 AND member_cv < 'Infinity'::float8 AND length(btrim(protocol_fingerprint)) > 0");
                    table.ForeignKey(
                        name: "fk_stationary_baseline_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stationary_baseline_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stationary_baseline_artifact_version_algorithm_version_id",
                        column: x => x.algorithm_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stationary_baseline_fixture_fixture_id",
                        column: x => x.fixture_id,
                        principalTable: "fixture",
                        principalColumn: "fixture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stationary_baseline_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lux_evaluation",
                columns: table => new
                {
                    lux_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    photo_id = table.Column<string>(type: "text", nullable: false),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    fixture_id = table.Column<string>(type: "text", nullable: true),
                    protocol_fingerprint = table.Column<string>(type: "text", nullable: false),
                    measured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    evaluated_by = table.Column<string>(type: "text", nullable: false),
                    cv_state = table.Column<string>(type: "text", nullable: false),
                    classified_as = table.Column<string>(type: "text", nullable: false),
                    lux_value = table.Column<double>(type: "double precision", nullable: false),
                    baseline_id = table.Column<long>(type: "bigint", nullable: true),
                    baseline_value = table.Column<double>(type: "double precision", nullable: true),
                    baseline_ratio = table.Column<double>(type: "double precision", nullable: true),
                    dim_threshold_ratio = table.Column<double>(type: "double precision", nullable: false),
                    dim_evaluation_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    baseline_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    expected_ratio = table.Column<double>(type: "double precision", nullable: true),
                    reason_codes = table.Column<string>(type: "jsonb", nullable: false),
                    algorithm_version_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lux_evaluation", x => x.lux_id);
                    table.CheckConstraint("ck_lux_evaluation_classification", "cv_state IN ('on','off') AND classified_as IN ('normal','dim','out') AND (classified_as <> 'out' OR cv_state = 'off') AND (classified_as NOT IN ('normal','dim') OR cv_state = 'on') AND (NOT dim_evaluation_eligible OR (baseline_id IS NOT NULL AND baseline_ratio IS NOT NULL AND cv_state = 'on')) AND (classified_as <> 'dim' OR dim_evaluation_eligible) AND (baseline_id IS NULL) = (baseline_value IS NULL) AND (NOT baseline_eligible OR (cv_state = 'on' AND classified_as = 'normal' AND expected_ratio IS NULL)) AND jsonb_typeof(reason_codes) = 'array'");
                    table.CheckConstraint("ck_lux_evaluation_classified_as", "\"classified_as\" IN ('normal', 'dim', 'out', 'unknown')");
                    table.CheckConstraint("ck_lux_evaluation_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_lux_evaluation_finite", "lux_value >= 0 AND lux_value < 'Infinity'::float8 AND (baseline_value IS NULL OR (baseline_value > 0 AND baseline_value < 'Infinity'::float8)) AND (baseline_ratio IS NULL OR (baseline_ratio >= 0 AND baseline_ratio < 'Infinity'::float8)) AND dim_threshold_ratio > 0 AND dim_threshold_ratio <= 1 AND (expected_ratio IS NULL OR (expected_ratio > 0 AND expected_ratio <= 1))");
                    table.ForeignKey(
                        name: "fk_lux_evaluation_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_app_user_evaluated_by",
                        column: x => x.evaluated_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_artifact_version_algorithm_version_id",
                        column: x => x.algorithm_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_fixture_fixture_id",
                        column: x => x.fixture_id,
                        principalTable: "fixture",
                        principalColumn: "fixture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_lux_reading_lux_id",
                        column: x => x.lux_id,
                        principalTable: "lux_reading",
                        principalColumn: "lux_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_pole_photo",
                        columns: x => new { x.photo_id, x.pole_id, x.commune_id },
                        principalTable: "work_order_pole_photo",
                        principalColumns: new[] { "photo_id", "pole_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_stationary_baseline",
                        columns: x => new { x.baseline_id, x.pole_id, x.commune_id },
                        principalTable: "stationary_baseline",
                        principalColumns: new[] { "baseline_id", "pole_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_evaluation_work_order_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_order",
                        principalColumn: "work_order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stationary_baseline_member",
                columns: table => new
                {
                    baseline_id = table.Column<long>(type: "bigint", nullable: false),
                    lux_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stationary_baseline_member", x => new { x.baseline_id, x.lux_id });
                    table.ForeignKey(
                        name: "fk_stationary_baseline_member_lux_reading_lux_id",
                        column: x => x.lux_id,
                        principalTable: "lux_reading",
                        principalColumn: "lux_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stationary_baseline_member_stationary_baseline_baseline_id",
                        column: x => x.baseline_id,
                        principalTable: "stationary_baseline",
                        principalColumn: "baseline_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lux_reading_protocol",
                table: "lux_reading",
                sql: "(sensor_height_m IS NULL) = (module_id IS NULL) AND (sensor_height_m IS NULL) = (window_spread IS NULL) AND (sensor_height_m IS NOT NULL OR (optical_filter_transmittance IS NULL AND module_firmware_version_id IS NULL)) AND (sensor_height_m IS NULL OR (sensor_height_m > 0 AND sensor_height_m < 'Infinity'::float8)) AND (window_spread IS NULL OR (window_spread >= 0 AND window_spread < 'Infinity'::float8)) AND (optical_filter_transmittance IS NULL OR (optical_filter_transmittance > 0 AND optical_filter_transmittance <= 1)) AND (module_id IS NULL OR length(btrim(module_id)) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault",
                sql: "origin_pole_photo_id IS NULL OR (source_channel IN ('cv','field_report') AND fault_type IN ('lamp_out','lamp_dim') AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL AND (source_channel <> 'field_report' OR (reported_by IS NOT NULL AND status_confidence IS NULL AND detection_model_version IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_algorithm_version_id",
                table: "lux_evaluation",
                column: "algorithm_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_baseline_id_pole_id_commune_id",
                table: "lux_evaluation",
                columns: new[] { "baseline_id", "pole_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_commune_id",
                table: "lux_evaluation",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_evaluated_by",
                table: "lux_evaluation",
                column: "evaluated_by");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_fixture_id",
                table: "lux_evaluation",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_photo_id",
                table: "lux_evaluation",
                column: "photo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_photo_id_pole_id_commune_id",
                table: "lux_evaluation",
                columns: new[] { "photo_id", "pole_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_pole_id_measured_at",
                table: "lux_evaluation",
                columns: new[] { "pole_id", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_pole_id_protocol_fingerprint",
                table: "lux_evaluation",
                columns: new[] { "pole_id", "protocol_fingerprint" },
                filter: "baseline_eligible");

            migrationBuilder.CreateIndex(
                name: "ix_lux_evaluation_work_order_id",
                table: "lux_evaluation",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_algorithm_version_id",
                table: "stationary_baseline",
                column: "algorithm_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_commune_id",
                table: "stationary_baseline",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_created_by",
                table: "stationary_baseline",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_fixture_id",
                table: "stationary_baseline",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_pole_id_version",
                table: "stationary_baseline",
                columns: new[] { "pole_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stationary_baseline_member_lux_id",
                table: "stationary_baseline_member",
                column: "lux_id");

            // Results and raw windows are immutable like the sweep's (luxmap_reject_processing_mutation, AddSurveyPublication).
            migrationBuilder.Sql("""
                CREATE TRIGGER lux_reading_sample_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON lux_reading_sample FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER stationary_baseline_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON stationary_baseline FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER stationary_baseline_member_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON stationary_baseline_member FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER lux_evaluation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON lux_evaluation FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never discard verdicts, baselines, raw windows or lamp_dim faults to make the old schema fit. Locks held to the end of
            // the migration transaction so the preflight cannot race a writer.
            migrationBuilder.Sql("""
                LOCK TABLE lux_reading, lux_evaluation, fault IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM lux_evaluation)
                        OR EXISTS (SELECT 1 FROM lux_reading WHERE sensor_height_m IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM fault WHERE origin_pole_photo_id IS NOT NULL AND fault_type = 'lamp_dim') THEN
                        RAISE EXCEPTION 'Cannot roll back AddStationaryLuxBaseline with stationary lux data; roll back the application and keep the schema.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "lux_evaluation");

            migrationBuilder.DropTable(
                name: "lux_reading_sample");

            migrationBuilder.DropTable(
                name: "stationary_baseline_member");

            migrationBuilder.DropTable(
                name: "stationary_baseline");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lux_reading_protocol",
                table: "lux_reading");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "module_firmware_version_id",
                table: "lux_reading");

            migrationBuilder.DropColumn(
                name: "module_id",
                table: "lux_reading");

            migrationBuilder.DropColumn(
                name: "optical_filter_transmittance",
                table: "lux_reading");

            migrationBuilder.DropColumn(
                name: "sensor_height_m",
                table: "lux_reading");

            migrationBuilder.DropColumn(
                name: "window_spread",
                table: "lux_reading");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault",
                sql: "origin_pole_photo_id IS NULL OR (source_channel IN ('cv','field_report') AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL AND (source_channel <> 'field_report' OR (reported_by IS NOT NULL AND status_confidence IS NULL AND detection_model_version IS NULL)))");
        }
    }
}
