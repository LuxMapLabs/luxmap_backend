using System.Globalization;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Settings;

/// <summary>
/// One setting a system admin changes without a deploy (BE-33, D-1). System-wide: no commune. The rows are seeded by the
/// <c>AddSystemSettings</c> migration and never deleted; this table is the ONLY source of these values (D-2) — they are not in
/// appsettings.
/// </summary>
public sealed class SystemSetting
{
    public required string Key { get; set; }

    public double Value { get; set; }

    /// <summary>Null together with <see cref="UpdatedBy"/> while the seeded default has never been changed.</summary>
    public DateTime? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

/// <summary>
/// One change of a setting (BE-33, D-3), append-only. <c>audit_event</c> cannot hold it — that table requires a commune — and a log
/// line is gone after 14 days, while a changed dim threshold changes research results for good.
/// </summary>
public sealed class SystemSettingChange : IImmutableRecord
{
    public long ChangeId { get; set; }

    public required string Key { get; set; }

    public double OldValue { get; set; }

    public double NewValue { get; set; }

    public DateTime ChangedAt { get; set; }

    public required string ChangedBy { get; set; }

    public string? Note { get; set; }

    public required string CorrelationId { get; set; }
}

/// <summary>What a key may hold. The CHECK constraints are generated from this list, so a change here shows up in the next migration.</summary>
public sealed record SystemSettingDefinition(string Key, double DefaultValue, double Min, double Max, string Unit, bool WholeNumber)
{
    public bool Accepts(double value)
        => double.IsFinite(value) && value >= Min && value <= Max && (!WholeNumber || value == Math.Truncate(value));
}

public static class SystemSettingKeys
{
    /// <summary>A lamp CV calls ON whose lux peak is below this share of its own baseline is <c>dim</c> (Phiếu v1.4 dòng 59, D-6).</summary>
    public const string DimThresholdRatio = "dim_threshold_ratio";

    /// <summary>A device silent for longer than this is <c>offline</c> and raises <c>node_offline</c> (I-3, D-6).</summary>
    public const string IotOfflineAfterMinutes = "iot_offline_after_minutes";

    public static readonly IReadOnlyList<SystemSettingDefinition> All =
    [
        new(DimThresholdRatio, 0.80, 0.50, 0.95, "ratio", WholeNumber: false),
        new(IotOfflineAfterMinutes, 60, 5, 1440, "minutes", WholeNumber: true),
    ];

    public static SystemSettingDefinition? Find(string key) => All.SingleOrDefault(d => d.Key == key);
}

/// <summary>
/// The values in force, read straight from the table — no cache (D-5), so the map and the <c>node_offline</c> job never use two
/// different thresholds in the same minute (I-3).
/// </summary>
public sealed record SystemSettingValues(double DimThresholdRatio, TimeSpan IotOfflineAfter)
{
    public static async Task<SystemSettingValues> ReadAsync(LuxMapDbContext db, CancellationToken ct)
    {
        var rows = await db.Set<SystemSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        double Value(string key) => rows.TryGetValue(key, out var value) ? value
            : throw new InvalidOperationException($"system_setting has no row '{key}'; the AddSystemSettings migration seeds it.");
        return new(Value(SystemSettingKeys.DimThresholdRatio), TimeSpan.FromMinutes(Value(SystemSettingKeys.IotOfflineAfterMinutes)));
    }
}

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_setting", table =>
        {
            table.HasCheckConstraint("ck_system_setting_key",
                $"key IN ({string.Join(", ", SystemSettingKeys.All.Select(d => $"'{d.Key}'"))})");
            table.HasCheckConstraint("ck_system_setting_value", SystemSettingSql.ValueInRange("key", "value"));
            table.HasCheckConstraint("ck_system_setting_updated_together", "(updated_at IS NULL) = (updated_by IS NULL)");
        });
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasColumnType("text");
        builder.Property(setting => setting.UpdatedBy).HasColumnType("text");
        builder.HasIndex(setting => setting.UpdatedBy);
    }
}

public sealed class SystemSettingChangeConfiguration : IEntityTypeConfiguration<SystemSettingChange>
{
    public void Configure(EntityTypeBuilder<SystemSettingChange> builder)
    {
        builder.ToTable("system_setting_change", table =>
        {
            table.HasCheckConstraint("ck_system_setting_change_old_value", SystemSettingSql.ValueInRange("key", "old_value"));
            table.HasCheckConstraint("ck_system_setting_change_new_value", SystemSettingSql.ValueInRange("key", "new_value"));
            table.HasCheckConstraint("ck_system_setting_change_note", "note IS NULL OR char_length(note) <= 500");
        });
        builder.HasKey(change => change.ChangeId);
        builder.Property(change => change.ChangeId).UseIdentityAlwaysColumn();
        builder.Property(change => change.Key).HasColumnType("text");
        builder.Property(change => change.ChangedBy).HasColumnType("text");
        builder.Property(change => change.Note).HasColumnType("text");
        builder.Property(change => change.CorrelationId).HasColumnType("text");
        builder.HasOne<SystemSetting>().WithMany().HasForeignKey(change => change.Key).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.Key, change.ChangeId });
        builder.HasIndex(change => change.ChangedBy);
    }
}

internal static class SystemSettingSql
{
    /// <summary>Finite (NaN sorts above every number in PostgreSQL) and inside the key's range; an unknown key holds nothing.</summary>
    public static string ValueInRange(string key, string value)
    {
        var cases = SystemSettingKeys.All.Select(d => string.Create(CultureInfo.InvariantCulture,
            $" WHEN '{d.Key}' THEN {value} BETWEEN {d.Min} AND {d.Max}{(d.WholeNumber ? $" AND {value} = trunc({value})" : "")}"));
        return $"{value} <> 'NaN'::float8 AND {value} <> 'Infinity'::float8 AND {value} <> '-Infinity'::float8"
            + $" AND CASE {key}{string.Concat(cases)} ELSE false END";
    }
}
