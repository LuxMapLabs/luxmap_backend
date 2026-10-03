using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "coverage_pct",
                table: "survey_sweep",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "processing_attempt",
                table: "survey_sweep",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_lease_expires_at",
                table: "survey_sweep",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "processing_lease_owner",
                table: "survey_sweep",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_survey_sweep_sweep_id_commune_id",
                table: "survey_sweep",
                columns: new[] { "sweep_id", "commune_id" });

            migrationBuilder.CreateTable(
                name: "artifact_version",
                columns: table => new
                {
                    version_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    component = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    artifact_hash = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artifact_version", x => x.version_id);
                    table.CheckConstraint("ck_artifact_version_component", "component IN ('clock_algorithm','association_algorithm')");
                    table.CheckConstraint("ck_artifact_version_hash", "artifact_hash ~ '^[0-9a-f]{64}$' AND length(version) > 0");
                    table.CheckConstraint("ck_artifact_version_metadata", "jsonb_typeof(metadata) = 'object'");
                });

            migrationBuilder.CreateTable(
                name: "survey_processing_run",
                columns: table => new
                {
                    run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    algorithm_version_id = table.Column<long>(type: "bigint", nullable: false),
                    clock_version_id = table.Column<long>(type: "bigint", nullable: false),
                    input_hash = table.Column<string>(type: "text", nullable: false),
                    settings_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    gis_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    clock_fit = table.Column<string>(type: "jsonb", nullable: false),
                    result_state = table.Column<string>(type: "text", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    coverage_pct = table.Column<double>(type: "double precision", nullable: true),
                    coverage_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_processing_run", x => x.run_id);
                    table.UniqueConstraint("ak_survey_processing_run_run_id_sweep_id", x => new { x.run_id, x.sweep_id });
                    table.CheckConstraint("ck_survey_run_attempt", "attempt > 0 AND finished_at >= started_at");
                    table.CheckConstraint("ck_survey_run_coverage", "coverage_pct IS NULL OR coverage_pct BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_survey_run_hash", "input_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_survey_run_json", "jsonb_typeof(settings_snapshot) = 'object' AND jsonb_typeof(gis_snapshot) = 'object' AND jsonb_typeof(clock_fit) = 'object'");
                    table.CheckConstraint("ck_survey_run_state", "result_state IN ('succeeded','failed') AND ((result_state = 'failed') = (error_code IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_survey_processing_run_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_processing_run_artifact_version_algorithm_version_id",
                        column: x => x.algorithm_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_processing_run_artifact_version_clock_version_id",
                        column: x => x.clock_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_processing_run_survey_sweep_sweep_id_commune_id",
                        columns: x => new { x.sweep_id, x.commune_id },
                        principalTable: "survey_sweep",
                        principalColumns: new[] { "sweep_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_pass",
                columns: table => new
                {
                    pass_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: false),
                    pass_no = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    from_fraction = table.Column<double>(type: "double precision", nullable: false),
                    to_fraction = table.Column<double>(type: "double precision", nullable: false),
                    start_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    end_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    quality_flags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_pass", x => x.pass_id);
                    table.UniqueConstraint("ak_survey_pass_pass_id_run_id", x => new { x.pass_id, x.run_id });
                    table.CheckConstraint("ck_survey_pass_direction", "(direction = 'forward' AND from_fraction < to_fraction) OR (direction = 'reverse' AND from_fraction > to_fraction)");
                    table.CheckConstraint("ck_survey_pass_fractions", "from_fraction BETWEEN 0 AND 1 AND to_fraction BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_survey_pass_quality", "jsonb_typeof(quality_flags) = 'object'");
                    table.CheckConstraint("ck_survey_pass_time", "start_elapsed_ns >= 0 AND end_elapsed_ns > start_elapsed_ns AND pass_no >= 0");
                    table.ForeignKey(
                        name: "fk_survey_pass_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_pass_survey_processing_run_run_id",
                        column: x => x.run_id,
                        principalTable: "survey_processing_run",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pole_observation",
                columns: table => new
                {
                    observation_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    pass_id = table.Column<long>(type: "bigint", nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    observed_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    chainage_m = table.Column<double>(type: "double precision", nullable: false),
                    peak_at_elapsed_ns = table.Column<long>(type: "bigint", nullable: true),
                    peak_lux = table.Column<double>(type: "double precision", nullable: true),
                    speed_mps = table.Column<double>(type: "double precision", nullable: false),
                    association_confidence = table.Column<double>(type: "double precision", nullable: false),
                    quality_flags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pole_observation", x => x.observation_id);
                    table.CheckConstraint("ck_pole_observation_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_pole_observation_finite", "chainage_m >= 0 AND chainage_m < 'Infinity'::float8 AND speed_mps >= 0 AND speed_mps < 'Infinity'::float8 AND association_confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_pole_observation_peak", "(peak_lux IS NULL) = (peak_at_elapsed_ns IS NULL) AND (peak_lux IS NULL OR (peak_lux >= 0 AND peak_lux < 'Infinity'::float8))");
                    table.CheckConstraint("ck_pole_observation_quality", "jsonb_typeof(quality_flags) = 'array'");
                    table.CheckConstraint("ck_pole_observation_time", "observed_elapsed_ns >= 0 AND (peak_at_elapsed_ns IS NULL OR peak_at_elapsed_ns >= 0)");
                    table.ForeignKey(
                        name: "fk_pole_observation_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_observation_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_observation_survey_pass_pass_id_run_id",
                        columns: x => new { x.pass_id, x.run_id },
                        principalTable: "survey_pass",
                        principalColumns: new[] { "pass_id", "run_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_observation_survey_processing_run_run_id",
                        column: x => x.run_id,
                        principalTable: "survey_processing_run",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_sweep_processing_status_processing_lease_expires_at",
                table: "survey_sweep",
                columns: new[] { "processing_status", "processing_lease_expires_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_sweep_coverage",
                table: "survey_sweep",
                sql: "coverage_pct IS NULL OR coverage_pct BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_sweep_lease",
                table: "survey_sweep",
                sql: "(processing_lease_owner IS NULL) = (processing_lease_expires_at IS NULL) AND processing_attempt >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_artifact_version_component_version",
                table: "artifact_version",
                columns: new[] { "component", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_commune_id",
                table: "pole_observation",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_pass_id_pole_id",
                table: "pole_observation",
                columns: new[] { "pass_id", "pole_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_pass_id_run_id",
                table: "pole_observation",
                columns: new[] { "pass_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_pole_id_observed_at",
                table: "pole_observation",
                columns: new[] { "pole_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_run_id",
                table: "pole_observation",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_pass_run_id_pass_no",
                table: "survey_pass",
                columns: new[] { "run_id", "pass_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_pass_segment_id",
                table: "survey_pass",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_algorithm_version_id",
                table: "survey_processing_run",
                column: "algorithm_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_clock_version_id",
                table: "survey_processing_run",
                column: "clock_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_commune_id",
                table: "survey_processing_run",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_sweep_id_attempt",
                table: "survey_processing_run",
                columns: new[] { "sweep_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_sweep_id_commune_id",
                table: "survey_processing_run",
                columns: new[] { "sweep_id", "commune_id" });
            migrationBuilder.Sql("""
                CREATE FUNCTION luxmap_reject_processing_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('luxmap.audit_purge', true) = 'on' THEN
                        RETURN NULL;
                    END IF;
                    RAISE EXCEPTION 'Processing records are immutable' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER artifact_version_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON artifact_version FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER survey_processing_run_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON survey_processing_run FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER survey_pass_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON survey_pass FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER pole_observation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON pole_observation FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pole_observation");

            migrationBuilder.DropTable(
                name: "survey_pass");

            migrationBuilder.DropTable(
                name: "survey_processing_run");

            migrationBuilder.DropTable(
                name: "artifact_version");

            migrationBuilder.Sql("DROP FUNCTION luxmap_reject_processing_mutation()");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_survey_sweep_sweep_id_commune_id",
                table: "survey_sweep");

            migrationBuilder.DropIndex(
                name: "ix_survey_sweep_processing_status_processing_lease_expires_at",
                table: "survey_sweep");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_sweep_coverage",
                table: "survey_sweep");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_sweep_lease",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "coverage_pct",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "processing_attempt",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "processing_lease_expires_at",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "processing_lease_owner",
                table: "survey_sweep");
        }
    }
}
