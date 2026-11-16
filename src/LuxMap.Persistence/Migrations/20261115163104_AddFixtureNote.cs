using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFixtureNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "fixture",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fixture_note_length",
                table: "fixture",
                sql: "char_length(note) <= 1000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the column would discard notes silently; clear them on purpose first.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM fixture WHERE note IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back AddFixtureNote while a fixture has a note';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_fixture_note_length",
                table: "fixture");

            migrationBuilder.DropColumn(
                name: "note",
                table: "fixture");
        }
    }
}
