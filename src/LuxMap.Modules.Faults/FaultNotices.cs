using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Faults;

/// <summary>The Vietnamese text of the fault notice (BE-27).</summary>
internal static class FaultNotices
{
    public static NotificationMessage Reported(Fault fault) => new(NotificationType.FaultReported, fault.CommuneId,
        NotificationEntityType.Fault, fault.FaultId,
        $"Sự cố mới {fault.FaultId}: {Type(fault.FaultType)}",
        $"Kỹ sư hiện trường báo {Type(fault.FaultType).ToLowerInvariant()} {(fault.PoleId is { } pole ? $"tại cột {pole}" : "tại một vị trí trên bản đồ")}, mức {Severity(fault.Severity)}.");

    private static string Type(FaultType type) => type switch
    {
        FaultType.LampOut => "Đèn tắt",
        FaultType.LampDim => "Đèn mờ",
        FaultType.SegmentOutage => "Mất điện cả đoạn",
        FaultType.NodeOffline => "Thiết bị mất kết nối",
        _ => "Giờ sáng suy giảm",
    };

    private static string Severity(Severity severity) => severity switch
    {
        Shared.Contracts.Enums.Severity.Low => "thấp",
        Shared.Contracts.Enums.Severity.Medium => "trung bình",
        Shared.Contracts.Enums.Severity.High => "cao",
        _ => "nghiêm trọng",
    };
}
