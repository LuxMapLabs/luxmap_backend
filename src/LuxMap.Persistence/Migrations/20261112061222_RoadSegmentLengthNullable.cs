using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoadSegmentLengthNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "length_m",
                table: "road_segment",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ASSET-SEGMENT-LENGTH-NULL: never invent a length (the generated defaultValue: 0 would) to make the rollback pass.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unmeasured_count bigint;
                BEGIN
                    SELECT count(*) INTO unmeasured_count FROM road_segment WHERE length_m IS NULL;
                    IF unmeasured_count > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back RoadSegmentLengthNullable: % segment(s) still have no length. Draw them and give them a length, or delete them, or roll back the application and keep the nullable schema.', unmeasured_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("ALTER TABLE road_segment ALTER COLUMN length_m SET NOT NULL;");
        }
    }
}
