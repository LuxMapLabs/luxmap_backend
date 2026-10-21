using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFixtureLampCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lamp_count",
                table: "fixture",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // The default only backfills the fittings that exist (one lamp each); the model has none, so EF always
            // writes the value and a stray 0 meets the CHECK instead of silently becoming 1 (ModelDefaultValueTests).
            migrationBuilder.Sql("ALTER TABLE fixture ALTER COLUMN lamp_count DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_lamp_count",
                table: "fixture",
                sql: "lamp_count BETWEEN 1 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_lamp_count",
                table: "fixture");

            migrationBuilder.DropColumn(
                name: "lamp_count",
                table: "fixture");
        }
    }
}
