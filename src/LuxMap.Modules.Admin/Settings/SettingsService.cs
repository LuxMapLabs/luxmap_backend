using System.Globalization;
using System.Net;
using LuxMap.Persistence;
using LuxMap.Persistence.Settings;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Admin.Settings;

/// <summary>
/// The system settings as the system admin changes them (BE-33). Each change is one row of <see cref="SystemSettingChange"/>, written
/// in the same <c>SaveChanges</c> as the new value (D-3). A new value applies from the next read — no cache (D-5): a survey run claimed
/// after the change, the next map request, the next <c>node_offline</c> pass. What was already published keeps the threshold it was
/// classified with; nothing is classified again.
/// </summary>
public sealed class SettingsService(
    LuxMapDbContext db, ICurrentActorAccessor actor, ICorrelationIdAccessor correlation, ILogger<SettingsService> logger)
{
    public const int NoteMaxLength = 500;

    public async Task<SettingsResponse> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<SystemSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, ct);
        return new([.. SystemSettingKeys.All.Select(d => Item(d, rows[d.Key]))]);
    }

    public async Task<SettingItem> UpdateAsync(string key, UpdateSettingRequest request, CancellationToken ct)
    {
        var definition = SystemSettingKeys.Find(key)
            ?? throw new LuxMapException(ErrorCodes.SettingNotFound, HttpStatusCode.NotFound, "No setting has that key.");
        var value = request.Value is { } v && definition.Accepts(v) ? v
            : throw Invalid("value", string.Create(CultureInfo.InvariantCulture,
                $"value is required, from {definition.Min} to {definition.Max}{(definition.WholeNumber ? ", a whole number" : "")}."));
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > NoteMaxLength) throw Invalid("note", $"note is at most {NoteMaxLength} characters.");
        var userId = actor.UserId ?? throw new InvalidOperationException("An authorized request carries a user id.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Two admins saving at once: the second waits, then reads the first one's value as its old value.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM system_setting WHERE key = {key} FOR UPDATE", ct);
        var setting = await db.Set<SystemSetting>().SingleAsync(s => s.Key == key, ct);
        if (setting.Value == value) return Item(definition, setting);

        var now = UtcMicrosecondClock.UtcNow();
        db.Set<SystemSettingChange>().Add(new SystemSettingChange
        {
            Key = key, OldValue = setting.Value, NewValue = value, ChangedAt = now, ChangedBy = userId, Note = note,
            CorrelationId = correlation.CorrelationId,
        });
        setting.Value = value;
        setting.UpdatedAt = now;
        setting.UpdatedBy = userId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Setting {Key} changed by {AdminId}.", key, userId);
        return Item(definition, setting);
    }

    private static SettingItem Item(SystemSettingDefinition d, SystemSetting row)
        => new(d.Key, row.Value, d.DefaultValue, d.Min, d.Max, d.Unit, row.UpdatedAt, row.UpdatedBy);

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });
}
