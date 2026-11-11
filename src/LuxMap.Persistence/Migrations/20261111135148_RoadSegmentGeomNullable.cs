using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadSegmentGeomNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<LineString>(
                name: "geom",
                table: "road_segment",
                type: "geometry(LineString,4326)",
                nullable: true,
                oldClrType: typeof(LineString),
                oldType: "geometry(LineString,4326)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROAD-SEGMENT-GEOM-NULL: never invent a route to make the rollback pass. Refuse while any segment has none.
            migrationBuilder.Sql("""
                DO $$
                DECLARE undrawn_count bigint;
                BEGIN
                    SELECT count(*) INTO undrawn_count FROM road_segment WHERE geom IS NULL;
                    IF undrawn_count > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back RoadSegmentGeomNullable: % segment(s) still have no geometry. Draw or delete them first, or roll back the application and keep the nullable schema.', undrawn_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("ALTER TABLE road_segment ALTER COLUMN geom SET NOT NULL;");
        }
    }
}
