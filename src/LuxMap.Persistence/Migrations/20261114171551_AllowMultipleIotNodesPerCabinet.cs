using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleIotNodesPerCabinet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_iot_node_cabinet_id",
                table: "iot_node");

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_cabinet_id",
                table: "iot_node",
                column: "cabinet_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to discard devices merely to restore the former cabinet-level uniqueness.
            migrationBuilder.Sql("""
                LOCK TABLE iot_node IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM iot_node GROUP BY cabinet_id HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot roll back AllowMultipleIotNodesPerCabinet while a cabinet carries multiple nodes; keep the schema or explicitly reassign the devices first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_iot_node_cabinet_id",
                table: "iot_node");

            migrationBuilder.CreateIndex(
                name: "ux_iot_node_cabinet_id",
                table: "iot_node",
                column: "cabinet_id",
                unique: true);
        }
    }
}
