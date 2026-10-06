using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetUpdatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drift POLE-NOTE N-4: the note loses its own author; pole.note_updated_by BECOMES pole.updated_by
            // (renamed, values kept — the last known editor of the pole). ⚠️ note_updated_at is DROPPED and
            // its values are gone: Down() rebuilds the column from updated_at, not from what it held.
            migrationBuilder.DropForeignKey(
                name: "fk_pole_app_user_note_updated_by",
                table: "pole");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_note_stamp_together",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "note_updated_at",
                table: "pole");

            migrationBuilder.RenameColumn(
                name: "note_updated_by",
                table: "pole",
                newName: "updated_by");

            migrationBuilder.RenameIndex(
                name: "ix_pole_note_updated_by",
                table: "pole",
                newName: "ix_pole_updated_by");

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                table: "road_segment",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                table: "fixture",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                table: "feeder",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_road_segment_updated_by",
                table: "road_segment",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_fixture_updated_by",
                table: "fixture",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_feeder_updated_by",
                table: "feeder",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "fk_feeder_app_user_updated_by",
                table: "feeder",
                column: "updated_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_fixture_app_user_updated_by",
                table: "fixture",
                column: "updated_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_app_user_updated_by",
                table: "pole",
                column: "updated_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_road_segment_app_user_updated_by",
                table: "road_segment",
                column: "updated_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_feeder_app_user_updated_by",
                table: "feeder");

            migrationBuilder.DropForeignKey(
                name: "fk_fixture_app_user_updated_by",
                table: "fixture");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_app_user_updated_by",
                table: "pole");

            migrationBuilder.DropForeignKey(
                name: "fk_road_segment_app_user_updated_by",
                table: "road_segment");

            migrationBuilder.DropIndex(
                name: "ix_road_segment_updated_by",
                table: "road_segment");

            migrationBuilder.DropIndex(
                name: "ix_fixture_updated_by",
                table: "fixture");

            migrationBuilder.DropIndex(
                name: "ix_feeder_updated_by",
                table: "feeder");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "road_segment");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "fixture");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "feeder");

            migrationBuilder.RenameColumn(
                name: "updated_by",
                table: "pole",
                newName: "note_updated_by");

            migrationBuilder.RenameIndex(
                name: "ix_pole_updated_by",
                table: "pole",
                newName: "ix_pole_note_updated_by");

            migrationBuilder.AddColumn<DateTime>(
                name: "note_updated_at",
                table: "pole",
                type: "timestamp with time zone",
                nullable: true);

            // The CHECK below demands who and when together; the old timestamps are lost, so the pole's own
            // updated_at stands in for every row that has an editor.
            migrationBuilder.Sql("UPDATE pole SET note_updated_at = updated_at WHERE note_updated_by IS NOT NULL;");

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
    }
}
