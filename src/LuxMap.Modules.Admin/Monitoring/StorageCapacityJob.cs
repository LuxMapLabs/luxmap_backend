using System.Globalization;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>
/// Every 15 minutes: free disk below <see cref="MonitoringOptions.LowDiskRatio"/> opens a spell and mails every active system admin
/// ONCE for it; back above the line closes the spell (BE-35, D-7).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mail, not an in-app notice</b> (Mỹ, 22/10/2026): <c>notification</c> belongs to a commune — its <c>commune_id</c> is required and
/// filtered — and a disk belongs to none. Mail also reaches an admin who is not looking at the app, which is the point of an alert.
/// </para>
/// <para>
/// 🔴 Once per spell comes from the database, not the lease: <see cref="StorageAlertConfiguration.OneOpen"/> lets one spell be open,
/// and the mail is CLAIMED by the one <c>UPDATE … WHERE notified_at IS NULL</c> that wins. A failed send puts the claim back so the next
/// run retries — an admin already mailed in that run may then get the mail twice, never zero times.
/// </para>
/// </remarks>
public sealed class StorageCapacityJob(
    IDiskProbe disk, NpgsqlDataSource dataSource, JobContextFactory contexts, IEmailSender mail, MonitoringOptions options,
    ILogger<StorageCapacityJob> log) : IScheduledJob
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public string Name => "storage_capacity";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        var space = disk.Measure();
        if (space.FreeRatio >= options.LowDiskRatio)
        {
            await using var close = dataSource.CreateCommand("UPDATE storage_alert SET closed_at = $1 WHERE closed_at IS NULL");
            close.Parameters.Add(new NpgsqlParameter { Value = now, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            if (await close.ExecuteNonQueryAsync(ct) > 0) log.LogInformation("Disk space is back above the line ({Correlation}).", correlationId);
            return;
        }

        await using (var open = dataSource.CreateCommand(
            "INSERT INTO storage_alert (opened_at, free_ratio_at_open) VALUES ($1, $2) ON CONFLICT DO NOTHING"))
        {
            open.Parameters.Add(new NpgsqlParameter { Value = now, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            open.Parameters.Add(new NpgsqlParameter { Value = space.FreeRatio });
            await open.ExecuteNonQueryAsync(ct);
        }

        long alertId;
        await using (var claim = dataSource.CreateCommand(
            "UPDATE storage_alert SET notified_at = $1 WHERE closed_at IS NULL AND notified_at IS NULL RETURNING alert_id"))
        {
            claim.Parameters.Add(new NpgsqlParameter { Value = now, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            if (await claim.ExecuteScalarAsync(ct) is not long claimed) return; // already mailed for this spell
            alertId = claimed;
        }

        try
        {
            var admins = await AdminsAsync(ct);
            foreach (var admin in admins) await mail.SendAsync(Message(admin, space), ct);
            log.LogWarning("Disk space low ({Free:P1} free); mailed {Count} system admins ({Correlation}).",
                space.FreeRatio, admins.Count, correlationId);
        }
        catch
        {
            await using var release = dataSource.CreateCommand("UPDATE storage_alert SET notified_at = NULL WHERE alert_id = $1");
            release.Parameters.Add(new NpgsqlParameter { Value = alertId });
            await release.ExecuteNonQueryAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Active = not locked and the password already set, the meaning <c>Notifier.ManagersCoveringAsync</c> uses.</summary>
    private async Task<List<AppUser>> AdminsAsync(CancellationToken ct)
    {
        await using var db = contexts.Unscoped();
        return await db.Set<AppUser>().AsNoTracking()
            .Where(user => user.Role == UserRole.SystemAdmin && !user.IsLocked && user.PasswordSetAt != null)
            .OrderBy(user => user.UserId.Length).ThenBy(user => user.UserId)
            .ToListAsync(ct);
    }

    private EmailMessage Message(AppUser admin, DiskSpace space)
    {
        var free = Gigabytes(space.FreeBytes);
        var total = Gigabytes(space.TotalBytes);
        var ratio = space.FreeRatio.ToString("P0", Vietnamese);
        var line = options.LowDiskRatio.ToString("P0", Vietnamese);
        return new(admin.Email, admin.FullName, $"[LuxMap] Đĩa máy chủ sắp đầy — còn {ratio}",
            $"""
            Chào {admin.FullName},

            Đĩa của máy chủ LuxMap còn trống {free} trên {total} ({ratio}), dưới ngưỡng cảnh báo {line}.

            Database, ảnh và video khảo sát, thư mục xử lý frame và bản sao lưu cùng nằm trên đĩa này. Khi đĩa đầy, nhận ảnh, nhận
            video và xử lý sweep sẽ lỗi.

            Xem chi tiết dung lượng theo từng bucket ở màn Quản trị hệ thống.

            Thư này gửi một lần mỗi khi dung lượng trống rơi xuống dưới ngưỡng.
            """);
    }

    private static string Gigabytes(long bytes) => (bytes / 1_073_741_824d).ToString("N1", Vietnamese) + " GB";
}
