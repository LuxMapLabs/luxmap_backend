using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFixtureSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fixture_pole_id_active",
                table: "fixture");

            migrationBuilder.AddColumn<short>(
                name: "slot",
                table: "fixture",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            // The default only backfills the fittings that exist: every pole had at most one lamp in service, so slot 1
            // is free of collisions. The model has none, so EF always writes the value and a stray 0 meets the CHECK
            // instead of silently becoming 1 (ModelDefaultValueTests).
            migrationBuilder.Sql("ALTER TABLE fixture ALTER COLUMN slot DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ux_fixture_pole_slot_active",
                table: "fixture",
                columns: new[] { "pole_id", "slot" },
                unique: true,
                filter: "removed_date IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_slot",
                table: "fixture",
                sql: "slot BETWEEN 1 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible once a pole carries two lamps in service: the one-lamp index recreated below then fails on
            // duplicate pole_id. That is deliberate — retire the second lamp of every such pole first, never lose a row.
            migrationBuilder.DropIndex(
                name: "ux_fixture_pole_slot_active",
                table: "fixture");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_slot",
                table: "fixture");

            migrationBuilder.DropColumn(
                name: "slot",
                table: "fixture");

            migrationBuilder.CreateIndex(
                name: "ux_fixture_pole_id_active",
                table: "fixture",
                column: "pole_id",
                unique: true,
                filter: "removed_date IS NULL");
        }
    }
}
