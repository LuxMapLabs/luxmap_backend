using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneActiveFixturePerPole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fixture_pole_id_active",
                table: "fixture");

            migrationBuilder.CreateIndex(
                name: "ux_fixture_pole_id_active",
                table: "fixture",
                column: "pole_id",
                unique: true,
                filter: "removed_date IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fixture_pole_id_active",
                table: "fixture");

            migrationBuilder.CreateIndex(
                name: "ix_fixture_pole_id_active",
                table: "fixture",
                column: "pole_id",
                filter: "removed_date IS NULL");
        }
    }
}
