using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "accepted_run_id",
                table: "survey_sweep",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "review_client_op_id",
                table: "survey_sweep",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "survey_sweep",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_request_hash",
                table: "survey_sweep",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "survey_sweep",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "survey_sweep",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "baseline_id",
                table: "pole_observation",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "baseline_value",
                table: "pole_observation",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_evaluated_at",
                table: "pole_current_status",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_run_id",
                table: "pole_current_status",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "origin_observation_id",
                table: "fault",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pole_observation_observation_id_pole_id_commune_id",
                table: "pole_observation",
                columns: new[] { "observation_id", "pole_id", "commune_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pole_observation_observation_id_pole_id_commune_id_run_id",
                table: "pole_observation",
                columns: new[] { "observation_id", "pole_id", "commune_id", "run_id" });

            migrationBuilder.CreateTable(
                name: "luminance_baseline",
                columns: table => new
                {
                    baseline_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    fixture_id = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    algorithm_version_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_luminance_baseline", x => x.baseline_id);
                    table.UniqueConstraint("ak_luminance_baseline_baseline_id_pole_id_commune_id", x => new { x.baseline_id, x.pole_id, x.commune_id });
                    table.CheckConstraint("ck_luminance_baseline_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_luminance_baseline_direction", "direction IN ('forward','reverse')");
                    table.CheckConstraint("ck_luminance_baseline_value", "value > 0 AND value < 'Infinity'::float8 AND version > 0 AND member_count > 0");
                    table.ForeignKey(
                        name: "fk_luminance_baseline_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_baseline_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_baseline_artifact_version_algorithm_version_id",
                        column: x => x.algorithm_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_baseline_fixture_fixture_id",
                        column: x => x.fixture_id,
                        principalTable: "fixture",
                        principalColumn: "fixture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_baseline_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "baseline_member",
                columns: table => new
                {
                    baseline_id = table.Column<long>(type: "bigint", nullable: false),
                    observation_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baseline_member", x => new { x.baseline_id, x.observation_id });
                    table.ForeignKey(
                        name: "fk_baseline_member_luminance_baseline_baseline_id",
                        column: x => x.baseline_id,
                        principalTable: "luminance_baseline",
                        principalColumn: "baseline_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_baseline_member_pole_observation_observation_id",
                        column: x => x.observation_id,
                        principalTable: "pole_observation",
                        principalColumn: "observation_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "luminance_history",
                columns: table => new
                {
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    observation_id = table.Column<long>(type: "bigint", nullable: true),
                    baseline_id = table.Column<long>(type: "bigint", nullable: true),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    peak_lux = table.Column<double>(type: "double precision", nullable: true),
                    cv_state = table.Column<string>(type: "text", nullable: true),
                    baseline_ratio = table.Column<double>(type: "double precision", nullable: true),
                    status_confidence = table.Column<double>(type: "double precision", nullable: true),
                    association_confidence = table.Column<double>(type: "double precision", nullable: false),
                    classified_as = table.Column<string>(type: "text", nullable: false),
                    dim_evaluation_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_by = table.Column<string>(type: "text", nullable: false),
                    reason_codes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_luminance_history", x => new { x.sweep_id, x.pole_id });
                    table.CheckConstraint("ck_luminance_history_classification", "(classified_as = 'unknown') = (status_confidence IS NULL) AND (classified_as = 'unknown' OR cv_state IS NOT NULL) AND (cv_state IS NULL OR cv_state IN ('on','off')) AND (classified_as <> 'out' OR cv_state = 'off') AND (classified_as NOT IN ('normal','dim') OR cv_state = 'on') AND (NOT dim_evaluation_eligible OR (baseline_id IS NOT NULL AND baseline_ratio IS NOT NULL AND cv_state = 'on')) AND (classified_as <> 'dim' OR dim_evaluation_eligible) AND jsonb_typeof(reason_codes) = 'array'");
                    table.CheckConstraint("ck_luminance_history_classified_as", "\"classified_as\" IN ('normal', 'dim', 'out', 'unknown')");
                    table.CheckConstraint("ck_luminance_history_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_luminance_history_finite", "(peak_lux IS NULL OR (peak_lux >= 0 AND peak_lux < 'Infinity'::float8)) AND (baseline_ratio IS NULL OR (baseline_ratio >= 0 AND baseline_ratio < 'Infinity'::float8)) AND association_confidence BETWEEN 0 AND 1 AND (status_confidence IS NULL OR status_confidence BETWEEN 0 AND 1)");
                    table.CheckConstraint("ck_luminance_history_unobserved", "observation_id IS NOT NULL OR (classified_as = 'unknown' AND peak_lux IS NULL AND cv_state IS NULL AND baseline_id IS NULL AND baseline_ratio IS NULL AND status_confidence IS NULL)");
                    table.ForeignKey(
                        name: "fk_luminance_history_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_history_app_user_published_by",
                        column: x => x.published_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_history_luminance_baseline_baseline_id_pole_id_co",
                        columns: x => new { x.baseline_id, x.pole_id, x.commune_id },
                        principalTable: "luminance_baseline",
                        principalColumns: new[] { "baseline_id", "pole_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_history_pole_observation_observation_id_pole_id_c",
                        columns: x => new { x.observation_id, x.pole_id, x.commune_id, x.run_id },
                        principalTable: "pole_observation",
                        principalColumns: new[] { "observation_id", "pole_id", "commune_id", "run_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_luminance_history_survey_processing_run_run_id_sweep_id",
                        columns: x => new { x.run_id, x.sweep_id },
                        principalTable: "survey_processing_run",
                        principalColumns: new[] { "run_id", "sweep_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_accepted_run_id_sweep_id",
                table: "survey_sweep",
                columns: new[] { "accepted_run_id", "sweep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_reviewed_by_review_client_op_id",
                table: "survey_sweep",
                columns: new[] { "reviewed_by", "review_client_op_id" },
                unique: true,
                filter: "review_client_op_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_sweep_review",
                table: "survey_sweep",
                sql: "(status IN ('accepted','returned')) = (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL AND review_client_op_id IS NOT NULL AND review_request_hash IS NOT NULL) AND (status = 'accepted') = (accepted_run_id IS NOT NULL) AND (status <> 'returned' OR (review_note IS NOT NULL AND length(btrim(review_note)) > 0))");

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_baseline_id_pole_id_commune_id",
                table: "pole_observation",
                columns: new[] { "baseline_id", "pole_id", "commune_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_observation_baseline_value",
                table: "pole_observation",
                sql: "(baseline_id IS NULL) = (baseline_value IS NULL) AND (baseline_value IS NULL OR (baseline_value > 0 AND baseline_value < 'Infinity'::float8))");

            migrationBuilder.CreateIndex(
                name: "ix_pole_current_status_last_run_id_last_sweep_id",
                table: "pole_current_status",
                columns: new[] { "last_run_id", "last_sweep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fault_origin_observation_id_pole_id_commune_id",
                table: "fault",
                columns: new[] { "origin_observation_id", "pole_id", "commune_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_observation_source",
                table: "fault",
                sql: "origin_observation_id IS NULL OR (source_channel = 'cv' AND pole_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_baseline_member_observation_id",
                table: "baseline_member",
                column: "observation_id");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_baseline_algorithm_version_id",
                table: "luminance_baseline",
                column: "algorithm_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_baseline_commune_id",
                table: "luminance_baseline",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_baseline_created_by",
                table: "luminance_baseline",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_baseline_fixture_id",
                table: "luminance_baseline",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_baseline_pole_id_version",
                table: "luminance_baseline",
                columns: new[] { "pole_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_baseline_id_pole_id_commune_id",
                table: "luminance_history",
                columns: new[] { "baseline_id", "pole_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_commune_id",
                table: "luminance_history",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_data_source",
                table: "luminance_history",
                column: "data_source");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_observation_id_pole_id_commune_id_run_id",
                table: "luminance_history",
                columns: new[] { "observation_id", "pole_id", "commune_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_pole_id_evaluated_at",
                table: "luminance_history",
                columns: new[] { "pole_id", "evaluated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_published_by",
                table: "luminance_history",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ix_luminance_history_run_id_sweep_id",
                table: "luminance_history",
                columns: new[] { "run_id", "sweep_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_fault_pole_observation_origin_observation_id_pole_id_commun",
                table: "fault",
                columns: new[] { "origin_observation_id", "pole_id", "commune_id" },
                principalTable: "pole_observation",
                principalColumns: new[] { "observation_id", "pole_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_current_status_survey_processing_run_last_run_id_last_",
                table: "pole_current_status",
                columns: new[] { "last_run_id", "last_sweep_id" },
                principalTable: "survey_processing_run",
                principalColumns: new[] { "run_id", "sweep_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_observation_luminance_baseline_baseline_id_pole_id_com",
                table: "pole_observation",
                columns: new[] { "baseline_id", "pole_id", "commune_id" },
                principalTable: "luminance_baseline",
                principalColumns: new[] { "baseline_id", "pole_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_sweep_app_user_reviewed_by",
                table: "survey_sweep",
                column: "reviewed_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_sweep_survey_processing_run_accepted_run_id_sweep_id",
                table: "survey_sweep",
                columns: new[] { "accepted_run_id", "sweep_id" },
                principalTable: "survey_processing_run",
                principalColumns: new[] { "run_id", "sweep_id" },
                onDelete: ReferentialAction.Restrict);
            // Same append-only guard as P2b; the transaction-local purge switch is for test teardown only.
            migrationBuilder.Sql("""
                CREATE TRIGGER luminance_baseline_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON luminance_baseline FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER baseline_member_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON baseline_member FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER luminance_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON luminance_history FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not erase published results or review decisions to roll back application code.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM luminance_history) OR EXISTS (SELECT 1 FROM luminance_baseline)
                        OR EXISTS (SELECT 1 FROM survey_sweep WHERE reviewed_at IS NOT NULL) THEN
                        RAISE EXCEPTION 'Retain survey publication data; rollback requires empty publication tables and no reviews';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_fault_pole_observation_origin_observation_id_pole_id_commun",
                table: "fault");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_current_status_survey_processing_run_last_run_id_last_",
                table: "pole_current_status");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_observation_luminance_baseline_baseline_id_pole_id_com",
                table: "pole_observation");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_sweep_app_user_reviewed_by",
                table: "survey_sweep");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_sweep_survey_processing_run_accepted_run_id_sweep_id",
                table: "survey_sweep");

            migrationBuilder.DropTable(
                name: "baseline_member");

            migrationBuilder.DropTable(
                name: "luminance_history");

            migrationBuilder.DropTable(
                name: "luminance_baseline");

            migrationBuilder.DropIndex(
                name: "ix_survey_sweep_accepted_run_id_sweep_id",
                table: "survey_sweep");

            migrationBuilder.DropIndex(
                name: "ix_survey_sweep_reviewed_by_review_client_op_id",
                table: "survey_sweep");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_sweep_review",
                table: "survey_sweep");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pole_observation_observation_id_pole_id_commune_id",
                table: "pole_observation");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pole_observation_observation_id_pole_id_commune_id_run_id",
                table: "pole_observation");

            migrationBuilder.DropIndex(
                name: "ix_pole_observation_baseline_id_pole_id_commune_id",
                table: "pole_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_observation_baseline_value",
                table: "pole_observation");

            migrationBuilder.DropIndex(
                name: "ix_pole_current_status_last_run_id_last_sweep_id",
                table: "pole_current_status");

            migrationBuilder.DropIndex(
                name: "ix_fault_origin_observation_id_pole_id_commune_id",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_observation_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "accepted_run_id",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "review_client_op_id",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "review_request_hash",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "baseline_id",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "baseline_value",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "last_evaluated_at",
                table: "pole_current_status");

            migrationBuilder.DropColumn(
                name: "last_run_id",
                table: "pole_current_status");

            migrationBuilder.DropColumn(
                name: "origin_observation_id",
                table: "fault");
        }
    }
}
