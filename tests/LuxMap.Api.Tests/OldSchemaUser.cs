using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// Inserts an account into a database migrated BELOW the current model. <c>db.Add(user)</c> would write every column the model knows,
/// including ones the older schema does not have yet (avatar_*), so migration tests create their accounts with SQL that names only
/// columns present in every schema they start from.
/// </summary>
internal static class OldSchemaUser
{
    public static async Task InsertAsync(LuxMapDbContext db, AppUser user)
    {
        var ids = await db.Database.SqlQuery<string>($"""
            INSERT INTO app_user (username, email, full_name, role, password_algorithm, password_hash, password_set_at)
            VALUES ({user.Username}, {user.Email}, {user.FullName}, {WireEnum.Name(user.Role)}, {user.PasswordAlgorithm},
                    {user.PasswordHash}::text, {user.PasswordSetAt}::timestamptz)
            RETURNING user_id AS "Value"
            """).ToListAsync();
        user.UserId = Assert.Single(ids);
    }
}
