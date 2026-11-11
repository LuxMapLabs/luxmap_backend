using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IotSourceRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "iot_registration_audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    server_id = table.Column<string>(type: "text", nullable: false),
                    node_id = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_registration_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "iot_server_registration",
                columns: table => new
                {
                    server_id = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_server_registration", x => x.server_id);
                });

            migrationBuilder.CreateTable(
                name: "iot_source_registration",
                columns: table => new
                {
                    node_id = table.Column<string>(type: "text", nullable: false),
                    server_id = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false),
                    profile = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_iot_source_registration", x => x.node_id);
                    table.ForeignKey(
                        name: "fk_iot_source_registration_iot_node_node_id",
                        column: x => x.node_id,
                        principalTable: "iot_node",
                        principalColumn: "node_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_iot_source_registration_server_id_device_id",
                table: "iot_source_registration",
                columns: new[] { "server_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iot_registration_audit");

            migrationBuilder.DropTable(
                name: "iot_server_registration");

            migrationBuilder.DropTable(
                name: "iot_source_registration");
        }
    }
}
