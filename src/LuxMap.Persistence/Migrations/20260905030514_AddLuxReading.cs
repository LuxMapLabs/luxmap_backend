using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLuxReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "lux_id_seq");

            migrationBuilder.CreateTable(
                name: "lux_reading",
                columns: table => new
                {
                    lux_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('LUX', nextval('lux_id_seq'), 4)"),
                    client_op_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    measured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lux_value = table.Column<double>(type: "double precision", nullable: false),
                    meter_model = table.Column<string>(type: "text", nullable: true),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    measured_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lux_reading", x => x.lux_id);
                    table.CheckConstraint("ck_lux_reading_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_lux_reading_value_non_negative", "lux_value >= 0");
                    table.ForeignKey(
                        name: "fk_lux_reading_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_reading_app_user_measured_by",
                        column: x => x.measured_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lux_reading_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lux_reading_commune_id",
                table: "lux_reading",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_lux_reading_data_source",
                table: "lux_reading",
                column: "data_source");

            migrationBuilder.CreateIndex(
                name: "ix_lux_reading_measured_by",
                table: "lux_reading",
                column: "measured_by");

            migrationBuilder.CreateIndex(
                name: "ix_lux_reading_pole_id_measured_at",
                table: "lux_reading",
                columns: new[] { "pole_id", "measured_at" });

            migrationBuilder.CreateIndex(
                name: "ux_lux_reading_client_op_id",
                table: "lux_reading",
                column: "client_op_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lux_reading");

            migrationBuilder.DropSequence(
                name: "lux_id_seq");
        }
    }
}
