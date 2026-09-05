using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetExternalRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_ref",
                table: "road_segment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_ref",
                table: "pole",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_ref",
                table: "feeder",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_road_segment_commune_external_ref",
                table: "road_segment",
                columns: new[] { "commune_id", "external_ref" },
                unique: true,
                filter: "external_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_pole_commune_external_ref",
                table: "pole",
                columns: new[] { "commune_id", "external_ref" },
                unique: true,
                filter: "external_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_feeder_commune_external_ref",
                table: "feeder",
                columns: new[] { "commune_id", "external_ref" },
                unique: true,
                filter: "external_ref IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_road_segment_commune_external_ref",
                table: "road_segment");

            migrationBuilder.DropIndex(
                name: "ux_pole_commune_external_ref",
                table: "pole");

            migrationBuilder.DropIndex(
                name: "ux_feeder_commune_external_ref",
                table: "feeder");

            migrationBuilder.DropColumn(
                name: "external_ref",
                table: "road_segment");

            migrationBuilder.DropColumn(
                name: "external_ref",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "external_ref",
                table: "feeder");
        }
    }
}
