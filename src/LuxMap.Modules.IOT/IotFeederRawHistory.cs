using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;

namespace LuxMap.Modules.IOT;

public sealed record IotFeederHistorySample(string NodeId, DateTime? MeasuredAt, DateTime ReceivedAt,
    string? ReceptionType, double Voltage, double CurrentMa, double PowerMw, string LampCommand);
public sealed record IotFeederHistoryResult(string FeederId, string SortBy, int Page, int PageSize, int Total,
    IReadOnlyList<IotFeederHistorySample> Items);

public sealed partial class IotDiagnosticsService
{
    public async Task<IotFeederHistoryResult> FeederRawHistoryAsync(string feederId, PageRequest page,
        string sortBy, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var start = from.HasValue ? new DateTimeOffset(UtcNormalization.ToUtc(from.Value)) : (DateTimeOffset?)null;
        var end = to.HasValue ? new DateTimeOffset(UtcNormalization.ToUtc(to.Value)) : (DateTimeOffset?)null;
        if (start.HasValue && end.HasValue && start >= end)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "from phải nhỏ hơn to.", new Dictionary<string, object?> { ["field"] = "to" });
        var history = await FeederTelemetryHistoryAsync(feederId, page, sortBy, ct, start, end);
        var items = history.Items.Select(sample =>
        {
            var data = sample.Reading.Data;
            if (!Valid(data.Voltage) || !Valid(data.Current) || !Valid(data.Power) ||
                data.LampCommand is not ("ON" or "OFF") || data.ReceptionType is not (null or "live" or "backfill"))
                throw Unavailable("IoT Bridge trả mẫu lịch sử điện không hợp lệ.");
            return new IotFeederHistorySample(sample.NodeId, MeasuredAt(data.Timestamp)?.UtcDateTime,
                sample.Reading.ReceivedAt.UtcDateTime, data.ReceptionType, data.Voltage!.Value,
                data.Current!.Value, data.Power!.Value, data.LampCommand);
        }).ToArray();
        return new(feederId, sortBy, page.Page, page.PageSize, history.Total, items);
    }
}
