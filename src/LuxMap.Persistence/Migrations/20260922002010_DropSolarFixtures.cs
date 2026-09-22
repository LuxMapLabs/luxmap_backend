using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropSolarFixtures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔴 DATA BEFORE CONSTRAINT. AddCheckConstraint validates every existing row, and this
            // database holds 45 solar fixtures from the FO-26 mock set — without this the migration
            // aborts on rows it does not name. Converting them is the decision of 22/09/2026, not a
            // convenience: solar lighting left the project scope, so a solar lamp is no longer a
            // thing the system can describe.
            migrationBuilder.Sql(
                """
                UPDATE fixture
                SET power_source = 'grid',
                    fixture_type = 'led_road_lamp',
                    updated_at = now()
                WHERE power_source <> 'grid' OR fixture_type <> 'led_road_lamp';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_fixture_type",
                table: "fixture");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_power_source",
                table: "fixture");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_fixture_type",
                table: "fixture",
                sql: "\"fixture_type\" IN ('led_road_lamp')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_power_source",
                table: "fixture",
                sql: "\"power_source\" IN ('grid')");
        }

        /// <inheritdoc />
        /// <remarks>
        /// ⚠️ <b>Down() restores the CONSTRAINTS but cannot restore the DATA, and that asymmetry is
        /// real.</b> Once the 45 solar rows are rewritten to grid, nothing in the schema records
        /// which they were — the conversion is lossy by nature. Rolling back reopens the enum so
        /// solar values are legal again; it does not bring any back. Re-seeding from
        /// <c>mocks/</c> is the only way to recover them, and after 22/09/2026 that mock set no
        /// longer carries any either.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_fixture_type",
                table: "fixture");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_power_source",
                table: "fixture");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_fixture_type",
                table: "fixture",
                sql: "\"fixture_type\" IN ('led_road_lamp', 'solar_all_in_one')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_power_source",
                table: "fixture",
                sql: "\"power_source\" IN ('grid', 'solar')");
        }
    }
}
