using System.Net;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Notifications;

/// <summary>Reads and marks the CALLER's notices. Nobody, the system admin included, reads another person's.</summary>
public sealed class NotificationService(LuxMapDbContext db, ICurrentActorAccessor actor)
{
    /// <summary>The one root every query here starts from: the commune filter applies, plus the recipient.</summary>
    private IQueryable<Notification> Mine()
    {
        var me = actor.UserId ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");
        return db.Set<Notification>().Where(notice => notice.RecipientUserId == me);
    }

    public async Task<NotificationPage> ListAsync(bool unreadOnly, PageRequest page, CancellationToken ct)
    {
        var rows = unreadOnly ? Mine().Where(notice => notice.ReadAt == null) : Mine();
        var total = await rows.CountAsync(ct);
        // Newest first; ties broken by the ID as a number (length, then text), never as plain text.
        var items = await rows.OrderByDescending(notice => notice.CreatedAt)
            .ThenByDescending(notice => notice.NotificationId.Length).ThenByDescending(notice => notice.NotificationId)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(notice => new NotificationItem(notice.NotificationId, notice.Type, notice.Title, notice.Body,
                notice.EntityType, notice.EntityId, notice.CreatedAt, notice.ReadAt))
            .ToListAsync(ct);
        return NotificationPage.From(PagedResult<NotificationItem>.From(page, total, items), await UnreadAsync(ct));
    }

    public Task<int> UnreadAsync(CancellationToken ct) => Mine().CountAsync(notice => notice.ReadAt == null, ct);

    /// <summary>Marks one notice read. Idempotent: reading it again keeps the first time. Someone else's is a 404.</summary>
    public async Task MarkReadAsync(string notificationId, CancellationToken ct)
    {
        var notice = await Mine().SingleOrDefaultAsync(row => row.NotificationId == notificationId, ct)
            ?? throw new LuxMapException(ErrorCodes.NotificationNotFound, HttpStatusCode.NotFound,
                "That notification does not exist, or it is not yours.");
        if (notice.ReadAt is not null) return;
        notice.ReadAt = UtcMicrosecondClock.UtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks every unread notice read, loaded and saved through the change tracker (bulk writes are banned).</summary>
    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var unread = await Mine().Where(notice => notice.ReadAt == null).ToListAsync(ct);
        if (unread.Count == 0) return;
        var now = UtcMicrosecondClock.UtcNow();
        foreach (var notice in unread) notice.ReadAt = now;
        await db.SaveChangesAsync(ct);
    }
}
