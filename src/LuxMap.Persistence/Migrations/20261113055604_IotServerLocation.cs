using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IotServerLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commune_id",
                table: "iot_server_registration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "iot_server_registration",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "iot_server_registration",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iot_server_registration_commune_id",
                table: "iot_server_registration",
                column: "commune_id");

            migrationBuilder.AddForeignKey(
                name: "fk_iot_server_registration_administrative_unit_commune_id",
                table: "iot_server_registration",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_iot_server_registration_administrative_unit_commune_id",
                table: "iot_server_registration");

            migrationBuilder.DropIndex(
                name: "ix_iot_server_registration_commune_id",
                table: "iot_server_registration");

            migrationBuilder.DropColumn(
                name: "commune_id",
                table: "iot_server_registration");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "iot_server_registration");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "iot_server_registration");
        }
    }
}
