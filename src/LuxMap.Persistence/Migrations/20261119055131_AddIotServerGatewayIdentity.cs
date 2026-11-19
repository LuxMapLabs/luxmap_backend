using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIotServerGatewayIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legacy_gateway_id",
                table: "iot_server_registration",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iot_server_registration_legacy_gateway_id",
                table: "iot_server_registration",
                column: "legacy_gateway_id",
                unique: true,
                filter: "legacy_gateway_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iot_server_registration_legacy_gateway_id",
                table: "iot_server_registration");

            migrationBuilder.DropColumn(
                name: "legacy_gateway_id",
                table: "iot_server_registration");
        }
    }
}
