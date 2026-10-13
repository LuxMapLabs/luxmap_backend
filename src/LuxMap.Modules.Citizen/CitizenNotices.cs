using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;

namespace LuxMap.Modules.Citizen;

/// <summary>Wording of the notices this module raises (BE-27: the owning module writes its own text).</summary>
internal static class CitizenNotices
{
    public static NotificationMessage Received(CitizenReport report, CitizenReportKind kind) => new(
        NotificationType.CitizenReportReceived, report.CommuneId, NotificationEntityType.CitizenReport, report.ReportId,
        $"Báo cáo mới từ người dân: {Kind(kind)}",
        $"Người dân báo {Kind(kind).ToLowerInvariant()} tại cột {report.PoleId} qua mã QR. Báo cáo đang chờ Quản lý duyệt.");

    private static string Kind(CitizenReportKind kind) => kind switch
    {
        CitizenReportKind.LampOut => "Đèn tắt",
        CitizenReportKind.LampDim => "Đèn mờ / chập chờn",
        CitizenReportKind.PoleDamaged => "Cột hỏng",
        _ => "Vấn đề khác",
    };
}
