using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkIotDiagnosticEpisodesToFaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fault_id",
                table: "iot_diagnostic_episode",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iot_diagnostic_episode_fault_id_commune_id",
                table: "iot_diagnostic_episode",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_iot_diagnostic_episode_fault_fault_id_commune_id",
                table: "iot_diagnostic_episode",
                columns: new[] { "fault_id", "commune_id" },
                principalTable: "fault",
                principalColumns: new[] { "fault_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_iot_diagnostic_episode_fault_fault_id_commune_id",
                table: "iot_diagnostic_episode");

            migrationBuilder.DropIndex(
                name: "ix_iot_diagnostic_episode_fault_id_commune_id",
                table: "iot_diagnostic_episode");

            migrationBuilder.DropColumn(
                name: "fault_id",
                table: "iot_diagnostic_episode");
        }
    }
}
