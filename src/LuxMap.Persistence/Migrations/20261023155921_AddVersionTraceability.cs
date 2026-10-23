using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_hash",
                table: "artifact_version");

            migrationBuilder.AddColumn<string>(
                name: "app_version",
                table: "survey_raw_file",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "camera_id",
                table: "survey_raw_file",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "module_firmware_version_id",
                table: "survey_raw_file",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_model",
                table: "survey_raw_file",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_id",
                table: "survey_raw_file",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "firmware_version_id",
                table: "iot_node",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artifact_hash",
                table: "artifact_version",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                table: "artifact_version",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "iot_node_firmware",
                columns: table => new
                {
                    change_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    node_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    firmware_version_id = table.Column<long>(type: "bigint", nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_node_firmware", x => x.change_id);
                    table.ForeignKey(
                        name: "fk_iot_node_firmware_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iot_node_firmware_artifact_version_firmware_version_id",
                        column: x => x.firmware_version_id,
                        principalTable: "artifact_version",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iot_node_firmware_iot_node_node_id_commune_id",
                        columns: x => new { x.node_id, x.commune_id },
                        principalTable: "iot_node",
                        principalColumns: new[] { "node_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_survey_raw_file_provenance",
                table: "survey_raw_file",
                sql: "(kind = 'capture_config' OR (profile_id IS NULL AND app_version IS NULL AND phone_model IS NULL AND camera_id IS NULL)) AND (kind <> 'gps_track' OR module_firmware_version_id IS NULL) AND (module_firmware_version_id IS NULL OR module_firmware_version_id >= 1) AND (profile_id IS NULL OR profile_id >= 1) AND char_length(coalesce(app_version, '')) <= 200 AND char_length(coalesce(phone_model, '')) <= 200 AND char_length(coalesce(camera_id, '')) <= 200");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_firmware_version_id",
                table: "iot_node",
                column: "firmware_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_artifact_version_created_by",
                table: "artifact_version",
                column: "created_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version",
                sql: "component IN ('clock_algorithm','association_algorithm','classification_algorithm','cv_model','frame_extractor','node_firmware','lux_module_firmware')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_hash",
                table: "artifact_version",
                sql: "length(version) > 0 AND ((artifact_hash IS NOT NULL AND artifact_hash ~ '^[0-9a-f]{64}$') OR (artifact_hash IS NULL AND component IN ('node_firmware','lux_module_firmware')))");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_firmware_commune_id",
                table: "iot_node_firmware",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_firmware_firmware_version_id",
                table: "iot_node_firmware",
                column: "firmware_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_firmware_node_id_commune_id",
                table: "iot_node_firmware",
                columns: new[] { "node_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_firmware_node_id_first_seen_at",
                table: "iot_node_firmware",
                columns: new[] { "node_id", "first_seen_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_artifact_version_app_user_created_by",
                table: "artifact_version",
                column: "created_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_iot_node_artifact_version_firmware_version_id",
                table: "iot_node",
                column: "firmware_version_id",
                principalTable: "artifact_version",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Restrict);

            // A firmware history row is a fact that happened, like every processing record: the same statement-level guard.
            migrationBuilder.Sql("""
                CREATE TRIGGER iot_node_firmware_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON iot_node_firmware FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The narrower CHECKs below cannot hold a firmware row, and artifact_version is immutable, so there is no way to clear
            // one first. Refuse with a reason instead of failing half-way through the rollback (traps/migrations.md).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM artifact_version WHERE component IN ('node_firmware', 'lux_module_firmware')
                               OR artifact_hash IS NULL OR created_by IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM iot_node_firmware)
                       OR EXISTS (SELECT 1 FROM iot_node WHERE firmware_version_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'AddVersionTraceability cannot be rolled back: firmware or registered versions are recorded';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_artifact_version_app_user_created_by",
                table: "artifact_version");

            migrationBuilder.DropForeignKey(
                name: "fk_iot_node_artifact_version_firmware_version_id",
                table: "iot_node");

            migrationBuilder.DropTable(
                name: "iot_node_firmware");

            migrationBuilder.DropCheckConstraint(
                name: "ck_survey_raw_file_provenance",
                table: "survey_raw_file");

            migrationBuilder.DropIndex(
                name: "ix_iot_node_firmware_version_id",
                table: "iot_node");

            migrationBuilder.DropIndex(
                name: "ix_artifact_version_created_by",
                table: "artifact_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version");

            migrationBuilder.DropCheckConstraint(
                name: "ck_artifact_version_hash",
                table: "artifact_version");

            migrationBuilder.DropColumn(
                name: "app_version",
                table: "survey_raw_file");

            migrationBuilder.DropColumn(
                name: "camera_id",
                table: "survey_raw_file");

            migrationBuilder.DropColumn(
                name: "module_firmware_version_id",
                table: "survey_raw_file");

            migrationBuilder.DropColumn(
                name: "phone_model",
                table: "survey_raw_file");

            migrationBuilder.DropColumn(
                name: "profile_id",
                table: "survey_raw_file");

            migrationBuilder.DropColumn(
                name: "firmware_version_id",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "artifact_version");

            // Not AlterColumn: EF would first run UPDATE … SET artifact_hash = '' WHERE … IS NULL, and the statement-level
            // immutability trigger refuses any UPDATE, even one touching no row. The guard above already proved no hash is NULL.
            migrationBuilder.Sql("ALTER TABLE artifact_version ALTER COLUMN artifact_hash SET NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_component",
                table: "artifact_version",
                sql: "component IN ('clock_algorithm','association_algorithm','classification_algorithm','cv_model','frame_extractor')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_artifact_version_hash",
                table: "artifact_version",
                sql: "artifact_hash ~ '^[0-9a-f]{64}$' AND length(version) > 0");
        }
    }
}
