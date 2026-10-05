using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.Survey.Entities;

namespace LuxMap.Modules.Survey;

/// <summary>The Vietnamese text of every survey notice (BE-27). Names the sweep and its work order, nothing from other communes.</summary>
internal static class SurveyNotices
{
    public static NotificationMessage Returned(SurveySweep sweep, string? note) => Message(NotificationType.SurveyReturned, sweep,
        $"Khảo sát {sweep.SweepId} bị trả lại", $"Phiếu {sweep.WorkOrderId}. Lý do: {note}");

    public static NotificationMessage ReadyForReview(SurveySweep sweep) => Message(NotificationType.SurveyReadyForReview, sweep,
        $"Khảo sát {sweep.SweepId} chờ duyệt", $"Phiếu {sweep.WorkOrderId}: đã xử lý xong, cần duyệt.");

    public static NotificationMessage ProcessingFailed(SurveySweep sweep, string? errorCode) => Message(NotificationType.SurveyProcessingFailed, sweep,
        $"Khảo sát {sweep.SweepId} xử lý thất bại", $"Phiếu {sweep.WorkOrderId}. Mã lỗi: {errorCode ?? "không rõ"}.");

    private static NotificationMessage Message(NotificationType type, SurveySweep sweep, string title, string body)
        => new(type, sweep.CommuneId, NotificationEntityType.SurveySweep, sweep.SweepId, title, body);
}
