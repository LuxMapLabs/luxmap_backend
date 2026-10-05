using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders.Entities;

namespace LuxMap.Modules.WorkOrders;

/// <summary>The Vietnamese text of every work-order notice (BE-27, D-2: the server writes it once for web and Android).</summary>
internal static class WorkOrderNotices
{
    public static NotificationMessage Assigned(WorkOrder wo) => Message(NotificationType.WorkOrderAssigned, wo,
        $"Bạn được giao phiếu {Kind(wo.TaskKind)} {wo.WorkOrderId}",
        wo.DueDate is { } due ? $"{Line(wo)}. Hạn {Notifier.Date(due)}." : $"{Line(wo)}.");

    public static NotificationMessage Unassigned(WorkOrder wo) => Message(NotificationType.WorkOrderUnassigned, wo,
        $"Phiếu {wo.WorkOrderId} không còn giao cho bạn", $"{Line(wo)}.");

    public static NotificationMessage Rescheduled(WorkOrder wo) => Message(NotificationType.WorkOrderRescheduled, wo,
        $"Phiếu {wo.WorkOrderId} đổi lịch", $"{Line(wo)}. Ngày làm: {Day(wo.ScheduledDate)}; hạn: {Day(wo.DueDate)}.");

    public static NotificationMessage Returned(WorkOrder wo, string? note) => Message(NotificationType.WorkOrderReturned, wo,
        $"Phiếu {wo.WorkOrderId} bị trả lại, cần làm lại", $"{Line(wo)}. Lý do: {note}");

    public static NotificationMessage Cancelled(WorkOrder wo, string? note) => Message(NotificationType.WorkOrderCancelled, wo,
        $"Phiếu {wo.WorkOrderId} đã bị huỷ", $"{Line(wo)}. Lý do: {note}");

    public static NotificationMessage Completed(WorkOrder wo) => Message(NotificationType.WorkOrderCompleted, wo,
        $"Phiếu {Kind(wo.TaskKind)} {wo.WorkOrderId} chờ nghiệm thu", $"{Line(wo)}. Kỹ sư đã báo hoàn thành.");

    public static NotificationMessage Verified(WorkOrder wo) => Message(NotificationType.WorkOrderVerified, wo,
        $"Phiếu {wo.WorkOrderId} đã được nghiệm thu", $"{Line(wo)}.");

    private static NotificationMessage Message(NotificationType type, WorkOrder wo, string title, string body)
        => new(type, wo.CommuneId, NotificationEntityType.WorkOrder, wo.WorkOrderId, title, body);

    private static string Line(WorkOrder wo) => $"{wo.WorkOrderId} — {wo.Title}";

    private static string Day(DateOnly? date) => date is { } value ? Notifier.Date(value) : "chưa đặt";

    private static string Kind(TaskKind kind) => kind switch
    {
        TaskKind.Repair => "sửa chữa",
        TaskKind.Inspection => "kiểm tra",
        _ => "khảo sát",
    };
}
