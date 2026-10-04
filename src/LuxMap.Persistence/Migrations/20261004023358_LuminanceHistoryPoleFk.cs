using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LuminanceHistoryPoleFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_luminance_history_pole_pole_id",
                table: "luminance_history",
                column: "pole_id",
                principalTable: "pole",
                principalColumn: "pole_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_luminance_history_pole_pole_id",
                table: "luminance_history");
        }
    }
}
