using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLuxReadingPhotoLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "photo_id",
                table: "lux_reading",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_lux_reading_photo_id",
                table: "lux_reading",
                columns: new[] { "photo_id", "pole_id", "commune_id" },
                unique: true,
                filter: "photo_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_lux_reading_pole_photo",
                table: "lux_reading",
                columns: new[] { "photo_id", "pole_id", "commune_id" },
                principalTable: "work_order_pole_photo",
                principalColumns: new[] { "photo_id", "pole_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lux_reading_pole_photo",
                table: "lux_reading");

            migrationBuilder.DropIndex(
                name: "ux_lux_reading_photo_id",
                table: "lux_reading");

            migrationBuilder.DropColumn(
                name: "photo_id",
                table: "lux_reading");
        }
    }
}
