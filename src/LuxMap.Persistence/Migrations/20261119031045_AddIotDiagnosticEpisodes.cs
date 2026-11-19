using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDiagnosticEpisodes : Migration
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

            migrationBuilder.AddColumn<DateTime>(
                name: "history_sampled_at",
                table: "iot_diagnostic_state",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "iot_diagnostic_episode",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    feeder_id = table.Column<string>(type: "text", nullable: false),
                    node_id = table.Column<string>(type: "text", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    signature = table.Column<string>(type: "text", nullable: false),
                    anomaly = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_diagnostic_episode", x => x.id);
                    table.CheckConstraint("ck_iot_diagnostic_episode_status", "status IN ('active', 'resolved', 'interrupted') AND ((status = 'active') = (closed_at IS NULL))");
                    table.CheckConstraint("ck_iot_diagnostic_episode_time", "ended_at >= started_at AND (closed_at IS NULL OR closed_at >= ended_at)");
                    table.ForeignKey(
                        name: "fk_iot_diagnostic_episode_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault",
                sql: "\"fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'current_anomaly', 'iot_hardware_fault')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault",
                sql: "\"override_fault_type\" IS NULL OR \"override_fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'current_anomaly', 'iot_hardware_fault')");

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_episode_commune_id",
                table: "iot_diagnostic_episode",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_episode_feeder_id_started_at_id",
                table: "iot_diagnostic_episode",
                columns: new[] { "feeder_id", "started_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_episode_node_id_code",
                table: "iot_diagnostic_episode",
                columns: new[] { "node_id", "code" },
                unique: true,
                filter: "closed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iot_diagnostic_episode");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "history_sampled_at",
                table: "iot_diagnostic_state");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_fault_type",
                table: "fault",
                sql: "\"fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'iot_hardware_fault')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault",
                sql: "\"override_fault_type\" IS NULL OR \"override_fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline', 'voltage_anomaly', 'iot_hardware_fault')");
        }
    }
}
