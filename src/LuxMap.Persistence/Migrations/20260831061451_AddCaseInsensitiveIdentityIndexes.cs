using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseInsensitiveIdentityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BE-06 indexes username and email on the RAW column, but BE-07 signs in with
            // lower(username) = lower(@input). That mismatch lets 'crew' and 'CREW' coexist, and the
            // sign-in query then matches TWO rows and picks an arbitrary one.
            //
            // Internal-only account creation never exercised this. Open registration turns it into an
            // attack: register 'Admin' to shadow the existing 'admin'.
            //
            // Raw SQL because EF Core cannot express an index over an expression. An application-level
            // check alone is NOT enough — two concurrent registrations would both pass the check and
            // both insert; only this index actually stops it.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_app_user_username_lower ON app_user (lower(username));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_app_user_email_lower ON app_user (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_app_user_username_lower;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_app_user_email_lower;");
        }
    }
}
