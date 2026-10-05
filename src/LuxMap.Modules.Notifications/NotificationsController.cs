using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Notifications;

/// <summary>
/// BE-27 — the caller's in-app notifications, read by polling. A push channel added later reads the same
/// rows; these endpoints stay.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    /// <summary>My notifications, newest first, with the unread count.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadNotifications)]
    [ProducesResponseType<NotificationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public Task<NotificationPage> ListAsync([FromQuery(Name = "unread_only")] bool? unreadOnly, PageQuery page, CancellationToken ct)
        => service.ListAsync(unreadOnly ?? false, page.ToPageRequest(), ct);

    /// <summary>Just the badge number — the cheap call to poll every 30–60 seconds.</summary>
    [HttpGet("unread-count")]
    [Authorize(Policy = LuxMapPolicies.ReadNotifications)]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken ct) => new(await service.UnreadAsync(ct));

    /// <summary>Marks one notification read. Idempotent.</summary>
    [HttpPost("{notificationId}/read")]
    [Authorize(Policy = LuxMapPolicies.ReadNotifications)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkReadAsync(string notificationId, CancellationToken ct)
    {
        await service.MarkReadAsync(notificationId, ct);
        return NoContent();
    }

    /// <summary>Marks all my unread notifications read.</summary>
    [HttpPost("read-all")]
    [Authorize(Policy = LuxMapPolicies.ReadNotifications)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllReadAsync(CancellationToken ct)
    {
        await service.MarkAllReadAsync(ct);
        return NoContent();
    }
}
