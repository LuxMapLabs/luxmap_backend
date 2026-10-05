using System.Globalization;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Notifications;

/// <summary>What one event tells its recipients: the same title and body for each of them.</summary>
public sealed record NotificationMessage(
    NotificationType Type, string CommuneId, NotificationEntityType EntityType, string EntityId, string Title, string Body);

/// <summary>
/// Stages notices on the CALLER's context (BE-27, D-8). It never saves: the caller's own
/// <c>SaveChanges</c> writes the notices with the change they report, or rolls both back.
/// </summary>
/// <remarks>
/// Static on purpose, with the context passed in: the survey worker is a singleton that opens its own
/// context per job and saves once per commune, so nothing scoped can be injected there.
/// </remarks>
public static class Notifier
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>
    /// One notice per distinct recipient, except the person who caused the event — nobody is told
    /// about what they just did themselves.
    /// </summary>
    public static void Stage(DbContext db, NotificationMessage message, IEnumerable<string?> recipients, string? actorUserId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(message);
        foreach (var recipient in recipients.OfType<string>().Distinct(StringComparer.Ordinal).Where(id => id != actorUserId))
        {
            db.Add(new Notification
            {
                RecipientUserId = recipient, CommuneId = message.CommuneId, Type = message.Type,
                Title = Clip(message.Title, 200), Body = Clip(message.Body, 1000),
                EntityType = message.EntityType, EntityId = message.EntityId, CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// Active managers holding EVERY one of <paramref name="communes"/> — the people who can act on the
    /// event. A manager missing one commune of a cross-commune survey could not review it, so is not told.
    /// </summary>
    /// <remarks>Active = not locked and the password already set, the same meaning as the account admin's.</remarks>
    public static async Task<List<string>> ManagersCoveringAsync(DbContext db, IReadOnlyCollection<string> communes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(communes);
        var wanted = communes.Distinct(StringComparer.Ordinal).ToArray();
        if (wanted.Length == 0) return [];
        var covering = db.Set<AppUserCommune>().Where(link => wanted.Contains(link.CommuneId))
            .GroupBy(link => link.UserId).Where(group => group.Count() == wanted.Length).Select(group => group.Key);
        return await db.Set<AppUser>()
            .Where(user => user.Role == UserRole.Manager && !user.IsLocked && user.PasswordSetAt != null && covering.Contains(user.UserId))
            .OrderBy(user => user.UserId.Length).ThenBy(user => user.UserId)
            .Select(user => user.UserId).ToListAsync(ct);
    }

    /// <summary>A date the way a Vietnamese reader writes it: 12/10/2026.</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Vietnamese);

    /// <summary>Cuts free text (a title, a manager's note) so the notice fits its column.</summary>
    public static string Clip(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
    }
}
