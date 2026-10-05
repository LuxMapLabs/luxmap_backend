using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPoleNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "pole",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "note_updated_at",
                table: "pole",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note_updated_by",
                table: "pole",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_note_updated_by",
                table: "pole",
                column: "note_updated_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_note_length",
                table: "pole",
                sql: "char_length(note) <= 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_note_stamp_together",
                table: "pole",
                sql: "(note_updated_by IS NULL) = (note_updated_at IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_app_user_note_updated_by",
                table: "pole",
                column: "note_updated_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pole_app_user_note_updated_by",
                table: "pole");

            migrationBuilder.DropIndex(
                name: "ix_pole_note_updated_by",
                table: "pole");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_note_length",
                table: "pole");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_note_stamp_together",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "note",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "note_updated_at",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "note_updated_by",
                table: "pole");
        }
    }
}
