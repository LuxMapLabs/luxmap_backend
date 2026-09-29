using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIotNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "node_id_seq");

            migrationBuilder.CreateTable(
                name: "iot_node",
                columns: table => new
                {
                    node_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('NODE', nextval('node_id_seq'), 3)"),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    node_role = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    supports_remote_control = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    last_report_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_node", x => x.node_id);
                    table.UniqueConstraint("ak_iot_node_node_id_commune_id", x => new { x.node_id, x.commune_id });
                    table.CheckConstraint("ck_iot_node_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_iot_node_data_source_not_field", "data_source IN ('calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_iot_node_node_role", "\"node_role\" IN ('segment_controller')");
                    table.ForeignKey(
                        name: "fk_iot_node_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feeder_control",
                columns: table => new
                {
                    feeder_id = table.Column<string>(type: "text", nullable: false),
                    node_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    relay_no = table.Column<short>(type: "smallint", nullable: false),
                    control_mode = table.Column<string>(type: "text", nullable: true),
                    mode_reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feeder_control", x => x.feeder_id);
                    table.CheckConstraint("ck_feeder_control_control_mode", "\"control_mode\" IS NULL OR \"control_mode\" IN ('on', 'off', 'auto')");
                    table.CheckConstraint("ck_feeder_control_mode_reported", "(control_mode IS NULL) = (mode_reported_at IS NULL)");
                    table.CheckConstraint("ck_feeder_control_relay_no_positive", "relay_no > 0");
                    table.ForeignKey(
                        name: "fk_feeder_control_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feeder_control_feeder_feeder_id_commune_id",
                        columns: x => new { x.feeder_id, x.commune_id },
                        principalTable: "feeder",
                        principalColumns: new[] { "feeder_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feeder_control_iot_node_node_id_commune_id",
                        columns: x => new { x.node_id, x.commune_id },
                        principalTable: "iot_node",
                        principalColumns: new[] { "node_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_control_commune_id",
                table: "feeder_control",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_feeder_control_feeder_id_commune_id",
                table: "feeder_control",
                columns: new[] { "feeder_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_control_node_id_commune_id",
                table: "feeder_control",
                columns: new[] { "node_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_feeder_control_node_id_relay_no",
                table: "feeder_control",
                columns: new[] { "node_id", "relay_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_commune_id",
                table: "iot_node",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_geom",
                table: "iot_node",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feeder_control");

            migrationBuilder.DropTable(
                name: "iot_node");

            migrationBuilder.DropSequence(
                name: "node_id_seq");
        }
    }
}
