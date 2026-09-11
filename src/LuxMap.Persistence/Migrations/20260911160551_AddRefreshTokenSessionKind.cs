using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenSessionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "session_kind",
                table: "refresh_token",
                type: "text",
                nullable: false,
                defaultValue: "mobile");

            // The default exists ONLY to backfill rows issued before this migration, and 'mobile' is the
            // right value for them: until the web endpoints existed, every token was issued through the
            // body-transport group. Keeping the default would be a trap — a write that forgot the kind
            // would silently get the 30-day mobile lifetime. Without it, such a write fails.
            migrationBuilder.Sql(@"ALTER TABLE refresh_token ALTER COLUMN session_kind DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_token_session_kind",
                table: "refresh_token",
                sql: "\"session_kind\" IN ('mobile', 'web_persistent', 'web_session')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_token_session_kind",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "session_kind",
                table: "refresh_token");
        }
    }
}
