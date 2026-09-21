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
