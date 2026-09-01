using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommuneForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seed_key",
                table: "administrative_unit",
                type: "text",
                nullable: true);

            // Adopt the commune BE-06 already seeded, before the unique index goes on.
            //
            // Without this the seeder finds no row for 'study_site' on the next run and creates a
            // SECOND commune, while the first one still holds every pole — which is exactly the trap
            // seed_key exists to close. Matched on the placeholder name because that is the only
            // thing identifying it; the column is unique, so this touches at most one row.
            migrationBuilder.Sql("""
                UPDATE administrative_unit
                SET seed_key = 'study_site'
                WHERE name = 'Commune 01' AND seed_key IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_administrative_unit_seed_key",
                table: "administrative_unit",
                column: "seed_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_feeder_administrative_unit_commune_id",
                table: "feeder",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_fixture_administrative_unit_commune_id",
                table: "fixture",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_administrative_unit_commune_id",
                table: "pole",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_current_status_administrative_unit_commune_id",
                table: "pole_current_status",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_road_segment_administrative_unit_commune_id",
                table: "road_segment",
                column: "commune_id",
                principalTable: "administrative_unit",
                principalColumn: "commune_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_feeder_administrative_unit_commune_id",
                table: "feeder");

            migrationBuilder.DropForeignKey(
                name: "fk_fixture_administrative_unit_commune_id",
                table: "fixture");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_administrative_unit_commune_id",
                table: "pole");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_current_status_administrative_unit_commune_id",
                table: "pole_current_status");

            migrationBuilder.DropForeignKey(
                name: "fk_road_segment_administrative_unit_commune_id",
                table: "road_segment");

            migrationBuilder.DropIndex(
                name: "ix_administrative_unit_seed_key",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "seed_key",
                table: "administrative_unit");
        }
    }
}
