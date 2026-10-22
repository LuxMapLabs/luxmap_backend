using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_setting",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_setting", x => x.key);
                    table.CheckConstraint("ck_system_setting_key", "key IN ('dim_threshold_ratio', 'iot_offline_after_minutes')");
                    table.CheckConstraint("ck_system_setting_updated_together", "(updated_at IS NULL) = (updated_by IS NULL)");
                    table.CheckConstraint("ck_system_setting_value", "value <> 'NaN'::float8 AND value <> 'Infinity'::float8 AND value <> '-Infinity'::float8 AND CASE key WHEN 'dim_threshold_ratio' THEN value BETWEEN 0.5 AND 0.95 WHEN 'iot_offline_after_minutes' THEN value BETWEEN 5 AND 1440 AND value = trunc(value) ELSE false END");
                    table.ForeignKey(
                        name: "fk_system_setting_app_user_updated_by",
                        column: x => x.updated_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_setting_change",
                columns: table => new
                {
                    change_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    key = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<double>(type: "double precision", nullable: false),
                    new_value = table.Column<double>(type: "double precision", nullable: false),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_setting_change", x => x.change_id);
                    table.CheckConstraint("ck_system_setting_change_new_value", "new_value <> 'NaN'::float8 AND new_value <> 'Infinity'::float8 AND new_value <> '-Infinity'::float8 AND CASE key WHEN 'dim_threshold_ratio' THEN new_value BETWEEN 0.5 AND 0.95 WHEN 'iot_offline_after_minutes' THEN new_value BETWEEN 5 AND 1440 AND new_value = trunc(new_value) ELSE false END");
                    table.CheckConstraint("ck_system_setting_change_note", "note IS NULL OR char_length(note) <= 500");
                    table.CheckConstraint("ck_system_setting_change_old_value", "old_value <> 'NaN'::float8 AND old_value <> 'Infinity'::float8 AND old_value <> '-Infinity'::float8 AND CASE key WHEN 'dim_threshold_ratio' THEN old_value BETWEEN 0.5 AND 0.95 WHEN 'iot_offline_after_minutes' THEN old_value BETWEEN 5 AND 1440 AND old_value = trunc(old_value) ELSE false END");
                    table.ForeignKey(
                        name: "fk_system_setting_change_app_user_changed_by",
                        column: x => x.changed_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_system_setting_change_system_setting_key",
                        column: x => x.key,
                        principalTable: "system_setting",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_setting_updated_by",
                table: "system_setting",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_system_setting_change_changed_by",
                table: "system_setting_change",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_system_setting_change_key_change_id",
                table: "system_setting_change",
                columns: new[] { "key", "change_id" });

            // D-2: the table is the only source, so it starts holding the values appsettings held until now. Literals on purpose — a
            // migration is history; SystemSettingKeys.DefaultValue may move later without rewriting what this one did.
            migrationBuilder.Sql("""
                INSERT INTO system_setting (key, value) VALUES ('dim_threshold_ratio', 0.8), ('iot_offline_after_minutes', 60);
                CREATE TRIGGER system_setting_change_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON system_setting_change FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_setting_change");

            migrationBuilder.DropTable(
                name: "system_setting");
        }
    }
}
