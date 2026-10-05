using LuxMap.Modules.Notifications.Entities;
using LuxMap.Shared.Contracts.Paging;

namespace LuxMap.Modules.Notifications;

/// <summary>One notice as <c>GET /notifications</c> returns it.</summary>
public sealed record NotificationItem(
    string NotificationId,
    NotificationType Type,
    string Title,
    string Body,
    NotificationEntityType EntityType,
    string EntityId,
    DateTime CreatedAt,
    DateTime? ReadAt);

/// <summary>The usual page plus the unread count, so one poll refreshes both the list and the badge.</summary>
public sealed record NotificationPage(int Page, int PageSize, int Total, int UnreadCount, IReadOnlyList<NotificationItem> Items)
{
    public static NotificationPage From(PagedResult<NotificationItem> page, int unread)
        => new(page.Page, page.PageSize, page.Total, unread, page.Items);
}

public sealed record UnreadCountResponse(int UnreadCount);
