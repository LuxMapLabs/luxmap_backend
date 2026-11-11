using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoleGeomNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Point>(
                name: "geom",
                table: "pole",
                type: "geometry(Point,4326)",
                nullable: true,
                oldClrType: typeof(Point),
                oldType: "geometry(Point,4326)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // POLE-GEOM-NULL: never invent a position to make the rollback pass. Refuse while any pole has none.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unlocated_count bigint;
                BEGIN
                    SELECT count(*) INTO unlocated_count FROM pole WHERE geom IS NULL;
                    IF unlocated_count > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back PoleGeomNullable: % pole(s) still have no position. Set their positions first, or roll back the application and keep the nullable schema.', unlocated_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("ALTER TABLE pole ALTER COLUMN geom SET NOT NULL;");
        }
    }
}
