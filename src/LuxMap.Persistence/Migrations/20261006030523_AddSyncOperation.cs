using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_operation",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    op_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_operation", x => new { x.user_id, x.client_op_id });
                    table.CheckConstraint("ck_sync_operation_op_type", "\"op_type\" IN ('fault_report', 'lux_reading', 'pole_note', 'work_order_start', 'work_order_complete')");
                    table.ForeignKey(
                        name: "fk_sync_operation_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_operation");
        }
    }
}
