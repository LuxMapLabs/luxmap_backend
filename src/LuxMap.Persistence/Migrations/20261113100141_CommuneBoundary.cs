using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommuneBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<MultiPolygon>(
                name: "boundary",
                table: "administrative_unit",
                type: "geometry(MultiPolygon,4326)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_administrative_unit_boundary_valid",
                table: "administrative_unit",
                sql: "boundary IS NULL OR (ST_IsValid(boundary) AND NOT ST_IsEmpty(boundary))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_administrative_unit_boundary_valid",
                table: "administrative_unit");

            migrationBuilder.DropColumn(
                name: "boundary",
                table: "administrative_unit");
        }
    }
}
