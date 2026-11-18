using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDiagnosticFaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault");

            migrationBuilder.AddColumn<string>(
                name: "cabinet_id",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "feeder_id",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iot_anomaly_code",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iot_evidence",
                table: "fault",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "recovered_at",
                table: "fault",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "iot_diagnostic_state",
                columns: table => new
                {
                    node_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    feeder_id = table.Column<string>(type: "text", nullable: true),
                    diagnostic_status = table.Column<string>(type: "text", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_diagnostic_state", x => x.node_id);
                    table.ForeignKey(
                        name: "fk_iot_diagnostic_state_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_iot_diagnostic_state_iot_node_node_id_commune_id",
                        columns: x => new { x.node_id, x.commune_id },
                        principalTable: "iot_node",
                        principalColumns: new[] { "node_id", "commune_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_fault_iot_diagnostic_active",
                table: "fault",
                columns: new[] { "commune_id", "origin_node_id", "feeder_id", "iot_anomaly_code" },
                unique: true,
                filter: "iot_anomaly_code IS NOT NULL AND fault_status IN ('detected','confirmed','in_progress','resolved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault",
                sql: "\"fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'iot_hardware_fault')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault",
                sql: "\"override_fault_type\" IS NULL OR \"override_fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'iot_hardware_fault')");

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_state_commune_id",
                table: "iot_diagnostic_state",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_state_node_id_commune_id",
                table: "iot_diagnostic_state",
                columns: new[] { "node_id", "commune_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM fault WHERE fault_type IN ('voltage_anomaly','iot_hardware_fault')) THEN RAISE EXCEPTION 'Cannot roll back IoT fault types while those faults exist'; END IF; END $$;");
            migrationBuilder.DropTable(
                name: "iot_diagnostic_state");

            migrationBuilder.DropIndex(
                name: "ux_fault_iot_diagnostic_active",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "cabinet_id",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "feeder_id",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "iot_anomaly_code",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "iot_evidence",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "recovered_at",
                table: "fault");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault",
                sql: "\"fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault",
                sql: "\"override_fault_type\" IS NULL OR \"override_fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline')");
        }
    }
}
