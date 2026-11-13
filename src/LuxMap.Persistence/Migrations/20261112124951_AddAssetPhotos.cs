using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "cabinet_asset_photo_id_seq");

            migrationBuilder.CreateSequence(
                name: "pole_asset_photo_id_seq");

            migrationBuilder.CreateTable(
                name: "cabinet_asset_photo",
                columns: table => new
                {
                    photo_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CAP', nextval('cabinet_asset_photo_id_seq'), 6)"),
                    cabinet_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cabinet_asset_photo", x => x.photo_id);
                    table.CheckConstraint("ck_cabinet_asset_photo_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_cabinet_asset_photo_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL)");
                    table.CheckConstraint("ck_cabinet_asset_photo_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_cabinet_asset_photo_keys", "object_key = 'original/' || photo_id || '.jpg' AND thumbnail_key = 'thumb/' || photo_id || '.jpg'");
                    table.CheckConstraint("ck_cabinet_asset_photo_operation", "client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_cabinet_asset_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
                    table.ForeignKey(
                        name: "fk_cabinet_asset_photo_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cabinet_asset_photo_app_user_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cabinet_asset_photo_app_user_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cabinet_asset_photo_electrical_cabinet_cabinet_id",
                        column: x => x.cabinet_id,
                        principalTable: "electrical_cabinet",
                        principalColumn: "cabinet_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cabinet_asset_photo_electrical_cabinet_cabinet_id_commune_id",
                        columns: x => new { x.cabinet_id, x.commune_id },
                        principalTable: "electrical_cabinet",
                        principalColumns: new[] { "cabinet_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pole_asset_photo",
                columns: table => new
                {
                    photo_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('PAP', nextval('pole_asset_photo_id_seq'), 6)"),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pole_asset_photo", x => x.photo_id);
                    table.CheckConstraint("ck_pole_asset_photo_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_pole_asset_photo_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL)");
                    table.CheckConstraint("ck_pole_asset_photo_hash", "request_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_pole_asset_photo_keys", "object_key = 'original/' || photo_id || '.jpg' AND thumbnail_key = 'thumb/' || photo_id || '.jpg'");
                    table.CheckConstraint("ck_pole_asset_photo_operation", "client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_pole_asset_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
                    table.ForeignKey(
                        name: "fk_pole_asset_photo_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_asset_photo_app_user_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_asset_photo_app_user_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_asset_photo_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('pole_asset_photo', 'cabinet_asset_photo', 'work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole', 'survey_recurring_schedule')");

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_asset_photo_cabinet_id_commune_id",
                table: "cabinet_asset_photo",
                columns: new[] { "cabinet_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_asset_photo_commune_id",
                table: "cabinet_asset_photo",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_asset_photo_deleted_by",
                table: "cabinet_asset_photo",
                column: "deleted_by");

            migrationBuilder.CreateIndex(
                name: "ix_cabinet_asset_photo_parent_order",
                table: "cabinet_asset_photo",
                columns: new[] { "cabinet_id", "created_at", "photo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cabinet_asset_photo_user_operation",
                table: "cabinet_asset_photo",
                columns: new[] { "uploaded_by", "client_op_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_asset_photo_commune_id",
                table: "pole_asset_photo",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_asset_photo_deleted_by",
                table: "pole_asset_photo",
                column: "deleted_by");

            migrationBuilder.CreateIndex(
                name: "ix_pole_asset_photo_parent_order",
                table: "pole_asset_photo",
                columns: new[] { "pole_id", "created_at", "photo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pole_asset_photo_user_operation",
                table: "pole_asset_photo",
                columns: new[] { "uploaded_by", "client_op_id" },
                unique: true);
            migrationBuilder.Sql("""
                ALTER TABLE pole_asset_photo ADD CONSTRAINT fk_pole_asset_photo_target
                    FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pole_asset_photo)
                       OR EXISTS (SELECT 1 FROM cabinet_asset_photo)
                       OR EXISTS (SELECT 1 FROM audit_event WHERE entity_type IN ('pole_asset_photo', 'cabinet_asset_photo')) THEN
                        RAISE EXCEPTION 'Asset photos or their audit exist; rollback must retain their schema.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "cabinet_asset_photo");

            migrationBuilder.DropTable(
                name: "pole_asset_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "cabinet_asset_photo_id_seq");

            migrationBuilder.DropSequence(
                name: "pole_asset_photo_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole', 'survey_recurring_schedule')");
        }
    }
}
