using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_work_order_id",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_work_order_id",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_parent_work_order_id_commune_id",
                table: "work_order",
                columns: new[] { "parent_work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_root_work_order_id_commune_id",
                table: "work_order",
                columns: new[] { "root_work_order_id", "commune_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_chain_complete",
                table: "work_order",
                sql: "(parent_work_order_id IS NULL) = (root_work_order_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_work_order_work_order_parent_work_order_id_commune_id",
                table: "work_order",
                columns: new[] { "parent_work_order_id", "commune_id" },
                principalTable: "work_order",
                principalColumns: new[] { "work_order_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_work_order_work_order_root_work_order_id_commune_id",
                table: "work_order",
                columns: new[] { "root_work_order_id", "commune_id" },
                principalTable: "work_order",
                principalColumns: new[] { "work_order_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_order_work_order_parent_work_order_id_commune_id",
                table: "work_order");

            migrationBuilder.DropForeignKey(
                name: "fk_work_order_work_order_root_work_order_id_commune_id",
                table: "work_order");

            migrationBuilder.DropIndex(
                name: "ix_work_order_parent_work_order_id_commune_id",
                table: "work_order");

            migrationBuilder.DropIndex(
                name: "ix_work_order_root_work_order_id_commune_id",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_chain_complete",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "parent_work_order_id",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "root_work_order_id",
                table: "work_order");
        }
    }
}
