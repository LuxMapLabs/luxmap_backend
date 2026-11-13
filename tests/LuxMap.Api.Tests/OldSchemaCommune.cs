using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// Inserts a commune into a database migrated BELOW the current model — the <see cref="OldSchemaUser"/> pattern. <c>db.Add(unit)</c>
/// would write every column the model knows, including ones the older schema does not have yet (<c>boundary</c>, COMMUNE-BOUNDARY),
/// so migration tests create their communes with SQL that names only the column present in every schema they start from.
/// </summary>
internal static class OldSchemaCommune
{
    public static async Task<string> InsertAsync(LuxMapDbContext db, string name)
    {
        var ids = await db.Database.SqlQuery<string>(
            $"INSERT INTO administrative_unit (name) VALUES ({name}) RETURNING commune_id AS \"Value\"").ToListAsync();
        return Assert.Single(ids);
    }
}
