namespace LuxMap.Modules.Admin.Settings;

/// <summary><c>GET /admin/settings</c>: every setting, in the order of <c>SystemSettingKeys.All</c>. Two keys, so no paging.</summary>
public sealed record SettingsResponse(IReadOnlyList<SettingItem> Items);

/// <summary>
/// One setting with the range the server accepts, so the form can say it before a 400 does. <c>updated_at</c> / <c>updated_by</c>
/// are null while the seeded default has never been changed.
/// </summary>
public sealed record SettingItem(
    string Key, double Value, double DefaultValue, double Min, double Max, string Unit, DateTime? UpdatedAt, string? UpdatedBy);

/// <summary><c>PUT /admin/settings/{key}</c>. <c>note</c> (≤ 500 characters) says why — it is kept with the change.</summary>
public sealed class UpdateSettingRequest
{
    public double? Value { get; init; }

    public string? Note { get; init; }
}
