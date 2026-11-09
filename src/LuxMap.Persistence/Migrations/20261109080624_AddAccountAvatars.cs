using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAvatars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_reconciliation_bucket",
                table: "storage_reconciliation");

            migrationBuilder.AddColumn<long>(
                name: "avatar_byte_count",
                table: "app_user",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_object_key",
                table: "app_user",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "avatar_thumbnail_bytes",
                table: "app_user",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_thumbnail_key",
                table: "app_user",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "avatar_updated_at",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_reconciliation_bucket",
                table: "storage_reconciliation",
                sql: "\"bucket\" IN ('survey', 'evidence', 'video', 'citizen', 'avatar')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_avatar_all_or_none",
                table: "app_user",
                sql: "(avatar_object_key IS NULL AND avatar_byte_count IS NULL AND avatar_thumbnail_key IS NULL AND avatar_thumbnail_bytes IS NULL AND avatar_updated_at IS NULL) OR (avatar_object_key IS NOT NULL AND avatar_byte_count IS NOT NULL AND avatar_byte_count >= 0 AND avatar_thumbnail_key IS NOT NULL AND avatar_thumbnail_bytes IS NOT NULL AND avatar_thumbnail_bytes >= 0 AND avatar_updated_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_storage_reconciliation_bucket",
                table: "storage_reconciliation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_avatar_all_or_none",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "avatar_byte_count",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "avatar_object_key",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "avatar_thumbnail_bytes",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "avatar_thumbnail_key",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "avatar_updated_at",
                table: "app_user");

            // Reports for the new bucket cannot satisfy the old CHECK; they are derived data and the next run rewrites them.
            migrationBuilder.Sql("DELETE FROM storage_reconciliation WHERE bucket = 'avatar';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_storage_reconciliation_bucket",
                table: "storage_reconciliation",
                sql: "\"bucket\" IN ('survey', 'evidence', 'video', 'citizen')");
        }
    }
}
