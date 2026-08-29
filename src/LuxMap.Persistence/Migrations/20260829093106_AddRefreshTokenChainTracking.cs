using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenChainTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "chain_absolute_expiry",
                table: "refresh_token",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "chain_id",
                table: "refresh_token",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "revoked_reason",
                table: "refresh_token",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_chain_id",
                table: "refresh_token",
                column: "chain_id");

            // AddColumn để lại DEFAULT vĩnh viễn trên cột. Giá trị đó chỉ dùng để lấp cho
            // hàng cũ (bảng đang rỗng), giữ lại là bẫy: quên set ChainId sẽ ghi toàn số 0 và
            // gộp nhầm các phiên không liên quan vào cùng một chuỗi.
            migrationBuilder.Sql(@"ALTER TABLE refresh_token ALTER COLUMN chain_id DROP DEFAULT;");
            migrationBuilder.Sql(@"ALTER TABLE refresh_token ALTER COLUMN chain_absolute_expiry DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token",
                sql: "\"revoked_reason\" IS NULL OR \"revoked_reason\" IN ('rotation', 'logout', 'reuse_detected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_token_chain_id",
                table: "refresh_token");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "chain_absolute_expiry",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "chain_id",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "revoked_reason",
                table: "refresh_token");
        }
    }
}
