using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <summary>
    /// Renames the four <c>user_role</c> values to those of registration form FA26SE222 v1.2
    /// (Contract v1.7, D-R6, 25/09/2026).
    /// </summary>
    /// <remarks>
    /// The mapping is one-to-one — <c>management_agency → superior</c>,
    /// <c>maintenance_engineer → manager</c>, <c>field_crew → field_engineer</c>,
    /// <c>administrator → system_admin</c> — so unlike <c>DropSolarFixtures</c> this migration is
    /// LOSSLESS in both directions: Down() restores every row, not just the constraint.
    /// <para>
    /// 🔴 The order is DROP CONSTRAINT → UPDATE → ADD CONSTRAINT. The old CHECK refuses the new values
    /// and the new CHECK refuses the old ones, so neither constraint can be in place while the rows are
    /// rewritten. Both steps run inside the migration's transaction, so no committed state ever holds
    /// the column unguarded.
    /// </para>
    /// <para>
    /// The UPDATEs key on the VALUE, never on a username: rows left behind by the test fixtures map
    /// exactly like the seeded accounts do. <c>has_system_wide_scope</c> is untouched — the '*' scope
    /// follows the role from administrator to system_admin.
    /// </para>
    /// </remarks>
    public partial class RenameUserRolesToRegistrationV12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_role",
                table: "app_user");

            migrationBuilder.Sql("UPDATE app_user SET role = 'superior', updated_at = now() WHERE role = 'management_agency';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'manager', updated_at = now() WHERE role = 'maintenance_engineer';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'field_engineer', updated_at = now() WHERE role = 'field_crew';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'system_admin', updated_at = now() WHERE role = 'administrator';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_role",
                table: "app_user",
                sql: "\"role\" IN ('superior', 'manager', 'field_engineer', 'system_admin')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_role",
                table: "app_user");

            migrationBuilder.Sql("UPDATE app_user SET role = 'management_agency', updated_at = now() WHERE role = 'superior';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'maintenance_engineer', updated_at = now() WHERE role = 'manager';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'field_crew', updated_at = now() WHERE role = 'field_engineer';");
            migrationBuilder.Sql("UPDATE app_user SET role = 'administrator', updated_at = now() WHERE role = 'system_admin';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_role",
                table: "app_user",
                sql: "\"role\" IN ('management_agency', 'maintenance_engineer', 'field_crew', 'administrator')");
        }
    }
}
