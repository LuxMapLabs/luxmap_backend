using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoleSegmentNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "segment_id",
                table: "pole",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE unassigned_count bigint;
                BEGIN
                    SELECT count(*) INTO unassigned_count FROM pole WHERE segment_id IS NULL;
                    IF unassigned_count > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back PoleSegmentNullable: % pole(s) still have no segment. Assign verified segments first, or roll back the application and keep the nullable schema.', unassigned_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("ALTER TABLE pole ALTER COLUMN segment_id SET NOT NULL;");
        }
    }
}
