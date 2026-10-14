using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommuneMapFrame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "bbox_max_lat",
                table: "administrative_unit",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bbox_max_lng",
                table: "administrative_unit",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bbox_min_lat",
                table: "administrative_unit",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bbox_min_lng",
                table: "administrative_unit",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_administrative_unit_bbox_together",
                table: "administrative_unit",
                sql: "(bbox_min_lng IS NULL) = (bbox_min_lat IS NULL) AND (bbox_min_lng IS NULL) = (bbox_max_lng IS NULL) AND (bbox_min_lng IS NULL) = (bbox_max_lat IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_administrative_unit_bbox_valid",
                table: "administrative_unit",
                sql: "bbox_min_lng IS NULL OR (bbox_min_lng <> 'NaN'::float8 AND bbox_min_lat <> 'NaN'::float8 AND bbox_max_lng <> 'NaN'::float8 AND bbox_max_lat <> 'NaN'::float8 AND bbox_min_lng >= -180 AND bbox_max_lng <= 180 AND bbox_min_lat >= -90 AND bbox_max_lat <= 90 AND bbox_min_lng < bbox_max_lng AND bbox_min_lat < bbox_max_lat)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_administrative_unit_bbox_together",
                table: "administrative_unit");

            migrationBuilder.DropCheckConstraint(
                name: "ck_administrative_unit_bbox_valid",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "bbox_max_lat",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "bbox_max_lng",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "bbox_min_lat",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "bbox_min_lng",
                table: "administrative_unit");
        }
    }
}
