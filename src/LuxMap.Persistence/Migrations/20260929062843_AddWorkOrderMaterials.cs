using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "materials_note",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "materials_used",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_materials_note_not_blank",
                table: "work_order",
                sql: "materials_note IS NULL OR btrim(materials_note) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_materials_used_not_blank",
                table: "work_order",
                sql: "materials_used IS NULL OR btrim(materials_used) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_materials_note_not_blank",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_materials_used_not_blank",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "materials_note",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "materials_used",
                table: "work_order");
        }
    }
}
