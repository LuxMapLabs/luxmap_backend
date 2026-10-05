using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.Notifications.Entities;

/// <summary>What happened, as the wire value a client switches on (BE-27).</summary>
public enum NotificationType
{
    WorkOrderAssigned,
    WorkOrderUnassigned,
    WorkOrderRescheduled,
    WorkOrderReturned,
    WorkOrderCancelled,
    WorkOrderCompleted,
    WorkOrderVerified,
    SurveyReturned,
    SurveyReadyForReview,
    SurveyProcessingFailed,
    FaultReported,
}

/// <summary>Which resource <see cref="Notification.EntityId"/> names, so a client knows where to navigate.</summary>
public enum NotificationEntityType { WorkOrder, SurveySweep, Fault }

/// <summary>
/// One notice for ONE person (BE-27). Staged in the same <c>SaveChanges</c> as the change it reports,
/// so a rolled-back change leaves no notice behind.
/// </summary>
/// <remarks>
/// <see cref="Title"/> and <see cref="Body"/> are a SNAPSHOT written when the event happened (D-3): renaming
/// the work order later does not rewrite what the engineer was told. <see cref="EntityId"/> has no foreign
/// key for the same reason — it names one of three tables, and opening it goes through that resource's own
/// endpoint, which decides whether the reader may still see it.
/// <para>
/// <see cref="ICommuneScoped"/> (D-4): a person moved out of a commune stops seeing its notices, under the
/// same token-lifetime rule as every other permission change.
/// </para>
/// </remarks>
public sealed class Notification : ICommuneScoped
{
    /// <summary><c>NTF-000001</c> — at least six digits (Contract 0.3).</summary>
    public string NotificationId { get; set; } = null!;

    public required string RecipientUserId { get; set; }

    public required string CommuneId { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>At most 200 characters (<c>ck_notification_title_length</c>).</summary>
    public required string Title { get; set; }

    /// <summary>At most 1000 characters (<c>ck_notification_body_length</c>).</summary>
    public required string Body { get; set; }

    public NotificationEntityType EntityType { get; set; }

    public required string EntityId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>NULL = unread. Set once; reading again keeps the first time.</summary>
    public DateTime? ReadAt { get; set; }
}
