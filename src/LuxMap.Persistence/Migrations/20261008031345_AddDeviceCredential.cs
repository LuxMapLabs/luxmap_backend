using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceCredential : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credential_hash",
                table: "iot_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "credential_set_at",
                table: "iot_node",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_iot_node_credential_set_together",
                table: "iot_node",
                sql: "(credential_hash IS NULL) = (credential_set_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_iot_node_credential_set_together",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "credential_hash",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "credential_set_at",
                table: "iot_node");
        }
    }
}
