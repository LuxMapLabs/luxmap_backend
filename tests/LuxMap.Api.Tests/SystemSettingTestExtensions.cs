using LuxMap.Persistence.Settings;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// A system setting changed for the length of one test, then put back (BE-33). Settings are system-wide: every class that reads one
/// sits in <see cref="AssetDatabaseCollection"/>, so nothing else runs while the value is changed — keep it that way.
/// </summary>
public static class SystemSettingTestExtensions
{
    public static async Task WithSettingAsync(this AssetImportFixture fixture, string key, double value, Func<Task> body)
    {
        var before = await fixture.SetSettingAsync(key, value);
        try
        {
            await body();
        }
        finally
        {
            await fixture.SetSettingAsync(key, before);
        }
    }

    /// <summary>Writes the value only — no history row, no <c>updated_by</c>: this is the test standing in for the admin.</summary>
    private static Task<double> SetSettingAsync(this AssetImportFixture fixture, string key, double value) => fixture.QueryAsync(async db =>
    {
        var setting = await db.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        var before = setting.Value;
        setting.Value = value;
        await db.SaveChangesAsync();
        return before;
    });
}
