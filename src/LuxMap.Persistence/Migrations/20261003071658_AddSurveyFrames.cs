using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyFrames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version");

            migrationBuilder.CreateSequence(
                name: "detection_id_seq");

            migrationBuilder.CreateSequence(
                name: "frame_id_seq");

            migrationBuilder.AddColumn<int>(
                name: "frame_count",
                table: "survey_sweep",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "classification_version_id",
                table: "survey_processing_run",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "detection_coverage_pct",
                table: "survey_processing_run",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "dim_coverage_pct",
                table: "survey_processing_run",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "extractor_version_id",
                table: "survey_processing_run",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "model_version_id",
                table: "survey_processing_run",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "baseline_ratio",
                table: "pole_observation",
                type: "double precision",
                nullable: true);

            // Rows written by phase 2b-1 have no CV classification yet: they are unknown. The default only
            // backfills them and is dropped at once — a lasting DB default would let EF omit Normal (the
            // enum's CLR default) from inserts and store every normal lamp as unknown.
            migrationBuilder.AddColumn<string>(
                name: "classified_as",
                table: "pole_observation",
                type: "text",
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.Sql("ALTER TABLE pole_observation ALTER COLUMN classified_as DROP DEFAULT;");

            migrationBuilder.AddColumn<double>(
                name: "cv_confidence",
                table: "pole_observation",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cv_state",
                table: "pole_observation",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "dim_evaluation_eligible",
                table: "pole_observation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reason_codes",
                table: "pole_observation",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "representative_frame_id",
                table: "pole_observation",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_survey_video_clip_clip_id_sweep_id",
                table: "survey_video_clip",
                columns: new[] { "clip_id", "sweep_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_survey_sweep_sweep_id_data_source",
                table: "survey_sweep",
                columns: new[] { "sweep_id", "data_source" });

            migrationBuilder.CreateTable(
                name: "survey_frame",
                columns: table => new
                {
                    frame_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('FRM', nextval('frame_id_seq'), 6)"),
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    clip_id = table.Column<long>(type: "bigint", nullable: false),
                    pts_ns = table.Column<long>(type: "bigint", nullable: false),
                    phone_elapsed_ns = table.Column<long>(type: "bigint", nullable: false),
                    extractor_version_id = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_frame", x => x.frame_id);
                    table.UniqueConstraint("ak_survey_frame_frame_id_sweep_id", x => new { x.frame_id, x.sweep_id });
                    table.CheckConstraint("ck_survey_frame_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_survey_frame_dimensions", "width > 0 AND height > 0 AND byte_count > 0 AND thumbnail_bytes > 0 AND phone_elapsed_ns >= 0");
                    table.CheckConstraint("ck_survey_frame_hash", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_survey_frame_artifact_version_extractor_version_id",
                        column: x => x.extractor_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_frame_survey_sweep_sweep_id_data_source",
                        columns: x => new { x.sweep_id, x.data_source },
                        principalTable: "survey_sweep",
                        principalColumns: new[] { "sweep_id", "data_source" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_frame_survey_video_clip_clip_id_sweep_id",
                        columns: x => new { x.clip_id, x.sweep_id },
                        principalTable: "survey_video_clip",
                        principalColumns: new[] { "clip_id", "sweep_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "detection",
                columns: table => new
                {
                    detection_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('DET', nextval('detection_id_seq'), 6)"),
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    sweep_id = table.Column<string>(type: "text", nullable: false),
                    frame_id = table.Column<string>(type: "text", nullable: false),
                    item_no = table.Column<int>(type: "integer", nullable: false),
                    cv_state = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    bbox_x = table.Column<double>(type: "double precision", nullable: false),
                    bbox_y = table.Column<double>(type: "double precision", nullable: false),
                    bbox_width = table.Column<double>(type: "double precision", nullable: false),
                    bbox_height = table.Column<double>(type: "double precision", nullable: false),
                    model_version_id = table.Column<long>(type: "bigint", nullable: false),
                    raw_prediction = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_detection", x => x.detection_id);
                    table.CheckConstraint("ck_detection_bbox", "confidence BETWEEN 0 AND 1 AND bbox_x BETWEEN 0 AND 1 AND bbox_y BETWEEN 0 AND 1 AND bbox_width > 0 AND bbox_width <= 1 AND bbox_height > 0 AND bbox_height <= 1 AND bbox_x + bbox_width <= 1 AND bbox_y + bbox_height <= 1");
                    table.CheckConstraint("ck_detection_raw", "jsonb_typeof(raw_prediction) = 'object'");
                    table.CheckConstraint("ck_detection_state", "cv_state IN ('on','off') AND item_no >= 0");
                    table.ForeignKey(
                        name: "fk_detection_artifact_version_model_version_id",
                        column: x => x.model_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_detection_survey_frame_frame_id_sweep_id",
                        columns: x => new { x.frame_id, x.sweep_id },
                        principalTable: "survey_frame",
                        principalColumns: new[] { "frame_id", "sweep_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_detection_survey_processing_run_run_id_sweep_id",
                        columns: x => new { x.run_id, x.sweep_id },
                        principalTable: "survey_processing_run",
                        principalColumns: new[] { "run_id", "sweep_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_sweep_frame_count",
                table: "survey_sweep",
                sql: "frame_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_classification_version_id",
                table: "survey_processing_run",
                column: "classification_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_extractor_version_id",
                table: "survey_processing_run",
                column: "extractor_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_processing_run_model_version_id",
                table: "survey_processing_run",
                column: "model_version_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_run_cv_coverage",
                table: "survey_processing_run",
                sql: "(detection_coverage_pct IS NULL OR detection_coverage_pct BETWEEN 0 AND 100) AND (dim_coverage_pct IS NULL OR dim_coverage_pct BETWEEN 0 AND 100)");

            migrationBuilder.CreateIndex(
                name: "ix_pole_observation_representative_frame_id",
                table: "pole_observation",
                column: "representative_frame_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_observation_classification",
                table: "pole_observation",
                sql: "jsonb_typeof(reason_codes) = 'array' AND (classified_as = 'unknown' OR (cv_state IS NOT NULL AND cv_confidence IS NOT NULL AND representative_frame_id IS NOT NULL)) AND (classified_as <> 'out' OR cv_state = 'off') AND (classified_as NOT IN ('normal','dim') OR cv_state = 'on') AND (NOT dim_evaluation_eligible OR (baseline_ratio IS NOT NULL AND cv_state = 'on')) AND (classified_as <> 'dim' OR dim_evaluation_eligible)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_observation_classified_as",
                table: "pole_observation",
                sql: "\"classified_as\" IN ('normal', 'dim', 'out', 'unknown')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_observation_cv",
                table: "pole_observation",
                sql: "(cv_state IS NULL OR cv_state IN ('on','off')) AND (cv_confidence IS NULL OR cv_confidence BETWEEN 0 AND 1) AND (baseline_ratio IS NULL OR (baseline_ratio >= 0 AND baseline_ratio < 'Infinity'::float8))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version",
                sql: "component IN ('clock_algorithm','association_algorithm','classification_algorithm','cv_model','frame_extractor')");

            migrationBuilder.CreateIndex(
                name: "ix_detection_frame_id_sweep_id",
                table: "detection",
                columns: new[] { "frame_id", "sweep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_detection_model_version_id",
                table: "detection",
                column: "model_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_detection_run_id_frame_id_item_no",
                table: "detection",
                columns: new[] { "run_id", "frame_id", "item_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_detection_run_id_sweep_id",
                table: "detection",
                columns: new[] { "run_id", "sweep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_frame_clip_id_pts_ns_extractor_version_id",
                table: "survey_frame",
                columns: new[] { "clip_id", "pts_ns", "extractor_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_survey_frame_clip_id_sweep_id",
                table: "survey_frame",
                columns: new[] { "clip_id", "sweep_id" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_frame_extractor_version_id",
                table: "survey_frame",
                column: "extractor_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_frame_sweep_id_data_source",
                table: "survey_frame",
                columns: new[] { "sweep_id", "data_source" });

            migrationBuilder.AddForeignKey(
                name: "fk_pole_observation_survey_frame_representative_frame_id",
                table: "pole_observation",
                column: "representative_frame_id",
                principalTable: "survey_frame",
                principalColumn: "frame_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_processing_run_artifact_version_classification_versi",
                table: "survey_processing_run",
                column: "classification_version_id",
                principalTable: "artifact_version",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_processing_run_artifact_version_extractor_version_id",
                table: "survey_processing_run",
                column: "extractor_version_id",
                principalTable: "artifact_version",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_processing_run_artifact_version_model_version_id",
                table: "survey_processing_run",
                column: "model_version_id",
                principalTable: "artifact_version",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Restrict);

            // Reuse P2b-1's immutable guard, including its transaction-local audit_purge teardown switch.
            migrationBuilder.Sql("""
                CREATE TRIGGER survey_frame_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON survey_frame FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                CREATE TRIGGER detection_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON detection FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old component CHECK cannot accept immutable media registry rows. Never delete them implicitly.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM artifact_version
                        WHERE component IN ('classification_algorithm','cv_model','frame_extractor')) THEN
                        RAISE EXCEPTION 'Stop the worker and retain media data; rollback requires an empty media artifact registry';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_pole_observation_survey_frame_representative_frame_id",
                table: "pole_observation");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_processing_run_artifact_version_classification_versi",
                table: "survey_processing_run");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_processing_run_artifact_version_extractor_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_processing_run_artifact_version_model_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropTable(
                name: "detection");

            migrationBuilder.DropTable(
                name: "survey_frame");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_survey_video_clip_clip_id_sweep_id",
                table: "survey_video_clip");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_survey_sweep_sweep_id_data_source",
                table: "survey_sweep");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_sweep_frame_count",
                table: "survey_sweep");

            migrationBuilder.DropIndex(
                name: "ix_survey_processing_run_classification_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropIndex(
                name: "ix_survey_processing_run_extractor_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropIndex(
                name: "ix_survey_processing_run_model_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_run_cv_coverage",
                table: "survey_processing_run");

            migrationBuilder.DropIndex(
                name: "ix_pole_observation_representative_frame_id",
                table: "pole_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_observation_classification",
                table: "pole_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_observation_classified_as",
                table: "pole_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_observation_cv",
                table: "pole_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version");

            migrationBuilder.DropColumn(
                name: "frame_count",
                table: "survey_sweep");

            migrationBuilder.DropColumn(
                name: "classification_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropColumn(
                name: "detection_coverage_pct",
                table: "survey_processing_run");

            migrationBuilder.DropColumn(
                name: "dim_coverage_pct",
                table: "survey_processing_run");

            migrationBuilder.DropColumn(
                name: "extractor_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropColumn(
                name: "model_version_id",
                table: "survey_processing_run");

            migrationBuilder.DropColumn(
                name: "baseline_ratio",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "classified_as",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "cv_confidence",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "cv_state",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "dim_evaluation_eligible",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "reason_codes",
                table: "pole_observation");

            migrationBuilder.DropColumn(
                name: "representative_frame_id",
                table: "pole_observation");

            migrationBuilder.DropSequence(
                name: "detection_id_seq");

            migrationBuilder.DropSequence(
                name: "frame_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version",
                sql: "component IN ('clock_algorithm','association_algorithm')");
        }
    }
}
