using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderPhotoCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_photo_copy",
                columns: table => new
                {
                    evidence_id = table.Column<string>(type: "text", nullable: false),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    photo_id = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_photo_copy", x => x.evidence_id);
                    table.CheckConstraint("ck_work_order_photo_copy_outcome", "\"outcome\" IN ('copied', 'skipped_limit', 'skipped_pixels', 'missing_object')");
                    table.CheckConstraint("ck_work_order_photo_copy_photo", "(outcome = 'copied') = (photo_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_work_order_photo_copy_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_photo_copy_pole_asset_photo_photo_id",
                        column: x => x.photo_id,
                        principalTable: "pole_asset_photo",
                        principalColumn: "photo_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_photo_copy_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_photo_copy_repair_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalTable: "repair_evidence",
                        principalColumn: "evidence_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_photo_copy_work_order",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_photo_copy_commune_id",
                table: "work_order_photo_copy",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_photo_copy_photo_id",
                table: "work_order_photo_copy",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_photo_copy_pole_id",
                table: "work_order_photo_copy",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_photo_copy_work_order_id_commune_id",
                table: "work_order_photo_copy",
                columns: new[] { "work_order_id", "commune_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_photo_copy");
        }
    }
}
