using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolePhotoWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "withdrawn_at",
                table: "work_order_pole_photo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "withdrawn_by",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_photo_withdrawn_by",
                table: "work_order_pole_photo",
                column: "withdrawn_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_withdrawal",
                table: "work_order_pole_photo",
                sql: "(withdrawn_at IS NULL AND withdrawn_by IS NULL) OR (withdrawn_at IS NOT NULL AND withdrawn_by IS NOT NULL AND decision IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_photo_withdrawn_by",
                table: "work_order_pole_photo",
                column: "withdrawn_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pole_photo_withdrawn_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_pole_photo_withdrawn_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_withdrawal",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "withdrawn_by",
                table: "work_order_pole_photo");
        }
    }
}
