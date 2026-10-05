using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminCreatedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "password_set_at",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            // Every account before BE-33a has a password. Backfill BEFORE the CHECK below, or it fails
            // on the existing rows. created_at is the best known answer to "when was it set".
            migrationBuilder.Sql("UPDATE app_user SET password_set_at = created_at WHERE password_hash IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "account_token",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_token", x => x.id);
                    table.CheckConstraint("ck_account_token_purpose", "\"purpose\" IN ('invite', 'reset')");
                    table.ForeignKey(
                        name: "fk_account_token_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token",
                sql: "\"revoked_reason\" IS NULL OR \"revoked_reason\" IN ('rotation', 'logout', 'reuse_detected', 'account_locked', 'password_reset')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_password_set_together",
                table: "app_user",
                sql: "(password_hash IS NULL) = (password_set_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_system_wide_scope_matches_role",
                table: "app_user",
                sql: "has_system_wide_scope = (role = 'system_admin')");

            migrationBuilder.CreateIndex(
                name: "ix_account_token_token_hash",
                table: "account_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_token_user_id",
                table: "account_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_token");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_password_set_together",
                table: "app_user");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_system_wide_scope_matches_role",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "password_set_at",
                table: "app_user");

            // An invited account has no hash; the old schema demands one. An empty hash never verifies,
            // so the account stays unable to sign in, as it was.
            migrationBuilder.Sql("UPDATE app_user SET password_hash = '' WHERE password_hash IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "app_user",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // The two new reasons have no old equivalent. 'logout' is the one that never triggers reuse
            // detection, which is how a locked or reset session must behave if it is ever replayed.
            migrationBuilder.Sql(
                "UPDATE refresh_token SET revoked_reason = 'logout' WHERE revoked_reason IN ('account_locked', 'password_reset');");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_token_revoked_reason",
                table: "refresh_token",
                sql: "\"revoked_reason\" IS NULL OR \"revoked_reason\" IN ('rotation', 'logout', 'reuse_detected')");
        }
    }
}
