using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeederCommuneCompositeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddForeignKey below VALIDATES EVERY EXISTING ROW. On a database that already holds a
            // pole wired to a feeder in another commune, this migration aborts with a bare 23503 that
            // names no row. Run this first to see the offenders — it returns nothing on a clean
            // database, and the dev database was verified empty of feeders before this was applied:
            //
            //   SELECT p.pole_id, p.commune_id AS pole_commune, f.feeder_id, f.commune_id AS feeder_commune
            //   FROM pole p JOIN feeder f ON f.feeder_id = p.feeder_id
            //   WHERE f.commune_id <> p.commune_id;
            //
            // Each row it returns has to be repaired by hand: the pole moves, or the circuit does.
            // There is no correct automatic answer, which is why this is a query and not a fix-up.
            migrationBuilder.DropForeignKey(
                name: "fk_pole_feeder_feeder_id",
                table: "pole");

            migrationBuilder.DropIndex(
                name: "ix_pole_feeder_id",
                table: "pole");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_feeder_feeder_id_commune_id",
                table: "feeder",
                columns: new[] { "feeder_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pole_feeder_id_commune_id",
                table: "pole",
                columns: new[] { "feeder_id", "commune_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_pole_feeder_feeder_id_commune_id",
                table: "pole",
                columns: new[] { "feeder_id", "commune_id" },
                principalTable: "feeder",
                principalColumns: new[] { "feeder_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pole_feeder_feeder_id_commune_id",
                table: "pole");

            migrationBuilder.DropIndex(
                name: "ix_pole_feeder_id_commune_id",
                table: "pole");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_feeder_feeder_id_commune_id",
                table: "feeder");

            migrationBuilder.CreateIndex(
                name: "ix_pole_feeder_id",
                table: "pole",
                column: "feeder_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_feeder_feeder_id",
                table: "pole",
                column: "feeder_id",
                principalTable: "feeder",
                principalColumn: "feeder_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
