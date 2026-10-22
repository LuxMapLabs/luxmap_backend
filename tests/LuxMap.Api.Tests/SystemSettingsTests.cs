using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Persistence.Settings;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-33 — the system settings the system admin changes without a deploy (<c>GET</c> / <c>PUT /admin/settings</c>), their range, and
/// the append-only history of every change.
/// </summary>
/// <remarks>
/// In <see cref="AssetDatabaseCollection"/>: a setting is system-wide, and every class that reads one (map, pole detail, survey
/// processing, <c>node_offline</c>) is in that collection, so none of them runs while a test here has a value changed. Every test
/// puts back what it changed. Changes are made as the seeded <c>admin</c>: history rows can never be deleted, so they must not point
/// at an account a fixture removes.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SystemSettingsTests(AssetImportFixture factory)
{
    private const string Url = "/api/v1/admin/settings";

    [Fact]
    public async Task The_list_has_both_settings_with_their_value_default_and_range()
    {
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");

        var items = (await admin.GetFromJsonAsync<JsonElement>(Url)).GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(["dim_threshold_ratio", "iot_offline_after_minutes"], items.Select(i => i.GetProperty("key").GetString()));
        var dim = items[0];
        Assert.Equal((0.8, 0.5, 0.95, "ratio"), (dim.GetProperty("default_value").GetDouble(), dim.GetProperty("min").GetDouble(),
            dim.GetProperty("max").GetDouble(), dim.GetProperty("unit").GetString()));
        var offline = items[1];
        Assert.Equal((60d, 5d, 1440d, "minutes"), (offline.GetProperty("default_value").GetDouble(), offline.GetProperty("min").GetDouble(),
            offline.GetProperty("max").GetDouble(), offline.GetProperty("unit").GetString()));
        Assert.All(items, i => Assert.True(i.TryGetProperty("updated_at", out _) && i.TryGetProperty("updated_by", out _)));
    }

    [Fact]
    public async Task A_change_is_read_back_at_once_and_kept_in_the_history_with_who_why_and_the_old_value()
    {
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        var before = await ValuesAsync();
        var correlation = Guid.NewGuid().ToString("N");
        var since = await LastChangeIdAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{Url}/dim_threshold_ratio")
            {
                Content = JsonContent.Create(new { value = 0.7, note = "  Bóng LED mới sáng đều hơn  " }),
            };
            request.Headers.Add(ApiHeaders.CorrelationId, correlation);
            var response = await admin.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var item = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0.7, item.GetProperty("value").GetDouble());
            var adminId = item.GetProperty("updated_by").GetString();
            Assert.StartsWith("USR-", adminId, StringComparison.Ordinal);
            Assert.Equal(JsonValueKind.String, item.GetProperty("updated_at").ValueKind);

            Assert.Equal(0.7, (await ValuesAsync()).DimThresholdRatio); // no cache: the next read already sees it (D-5)
            var change = Assert.Single(await ChangesSinceAsync(since));
            Assert.Equal(("dim_threshold_ratio", before.DimThresholdRatio, 0.7, adminId, "Bóng LED mới sáng đều hơn", correlation),
                (change.Key, change.OldValue, change.NewValue, change.ChangedBy, change.Note, change.CorrelationId));
        }
        finally
        {
            await PutAsync(admin, "dim_threshold_ratio", new { value = before.DimThresholdRatio });
        }
    }

    [Fact]
    public async Task Sending_the_current_value_changes_nothing_and_records_nothing()
    {
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        var current = (await ValuesAsync()).IotOfflineAfter.TotalMinutes;
        var since = await LastChangeIdAsync();

        var response = await PutAsync(admin, "iot_offline_after_minutes", new { value = current });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ChangesSinceAsync(since));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "dim_threshold_ratio", """{"value": 0.96}""" },
        { "dim_threshold_ratio", """{"value": 0.49}""" },
        { "dim_threshold_ratio", """{"value": "NaN"}""" },
        { "dim_threshold_ratio", """{}""" },
        { "dim_threshold_ratio", """{"value": null}""" },
        { "iot_offline_after_minutes", """{"value": 4}""" },
        { "iot_offline_after_minutes", """{"value": 1441}""" },
        { "iot_offline_after_minutes", """{"value": 30.5}""" },
        { "iot_offline_after_minutes", $$"""{"value": 30, "note": "{{new string('x', 501)}}"}""" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_value_outside_the_range_is_refused_and_nothing_is_written(string key, string body)
    {
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        var before = await ValuesAsync();
        var since = await LastChangeIdAsync();

        var response = await admin.PutAsync($"{Url}/{key}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ValuesAsync());
        Assert.Empty(await ChangesSinceAsync(since));
    }

    [Fact]
    public async Task An_unknown_key_is_404()
    {
        var admin = await factory.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");

        var response = await PutAsync(admin, "out_threshold_ratio", new { value = 0.15 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(ErrorCodes.SettingNotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("manager", "engineer", "SEED_ENGINEER_PASSWORD")]
    [InlineData("superior", "agency", "SEED_AGENCY_PASSWORD")]
    public async Task Only_the_system_admin_may_change_a_setting(string role, string username, string passwordVariable)
    {
        var client = await factory.SeededClientAsync(username, passwordVariable);

        var response = await PutAsync(client, "dim_threshold_ratio", new { value = 0.7 });

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} got {(int)response.StatusCode}");
        Assert.Contains(ErrorCodes.RoleForbidden, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("UPDATE system_setting_change SET note = 'rewritten'")]
    [InlineData("DELETE FROM system_setting_change")]
    [InlineData("TRUNCATE system_setting_change")]
    public async Task The_history_is_append_only(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.QueryAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            return await db.Database.ExecuteSqlRawAsync(sql);
        }));

        Assert.Equal("55000", error.SqlState);
    }

    [Fact]
    public async Task The_database_refuses_a_value_the_api_would_refuse()
    {
        foreach (var (key, value) in new[] { ("dim_threshold_ratio", double.NaN), ("dim_threshold_ratio", 0.96), ("iot_offline_after_minutes", 30.5) })
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => factory.QueryAsync(async db =>
            {
                (await db.Set<SystemSetting>().SingleAsync(s => s.Key == key)).Value = value;
                return await db.SaveChangesAsync();
            }));
            Assert.Equal("ck_system_setting_value", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        }
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, object body) => client.PutAsJsonAsync($"{Url}/{key}", body);

    private Task<SystemSettingValues> ValuesAsync() => factory.QueryAsync(db => SystemSettingValues.ReadAsync(db, default));

    private Task<long> LastChangeIdAsync()
        => factory.QueryAsync(async db => await db.Set<SystemSettingChange>().MaxAsync(c => (long?)c.ChangeId) ?? 0);

    private Task<List<SystemSettingChange>> ChangesSinceAsync(long changeId) => factory.QueryAsync(db =>
        db.Set<SystemSettingChange>().AsNoTracking().Where(c => c.ChangeId > changeId).OrderBy(c => c.ChangeId).ToListAsync());
}
