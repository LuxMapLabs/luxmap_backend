using System.Net.Http.Json;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.IOT;

public sealed partial class IotDiagnosticsService
{
    private sealed record AutoHistoryWindow(string DeviceId, string FeederId, DateTimeOffset From, DateTimeOffset? Until);
    private sealed record AutoHistoryEvent(string EventId, string DeviceId, string FeederId, string Command,
        DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string? AutoType = null);

    private async Task<IReadOnlyList<IotCommandHistoryItem>> ReadAutoHistoryAsync(string nodeId, CancellationToken ct)
    {
        var bindings = await registry.AutoHistoryBindingsAsync(nodeId, ct);
        if (bindings.Count == 0) return [];
        var settings = await registry.ReadAsync([], ct);
        var items = new List<IotCommandHistoryItem>();
        foreach (var group in bindings.GroupBy(binding => binding.ServerId))
        {
            var address = settings.Servers.TryGetValue(group.Key, out var server) ? server.BaseUrl
                : group.Key == "default" ? settings.BaseUrl : null;
            if (!IotOrigin.TryAllowed(settings, address, out var uri))
                throw Unavailable("Chưa cấu hình Bridge để đọc lịch sử AUTO.");
            var windows = group.Select(b => new AutoHistoryWindow(b.DeviceId, b.FeederId, b.AssignedAt, b.Until)).ToArray();
            try
            {
                using var response = await client.PostAsJsonAsync(new Uri(uri, "api/luxmap/devices/auto-transitions"), windows, ct);
                response.EnsureSuccessStatusCode();
                if (!IsJson(response)) throw Unavailable("Bridge không trả JSON cho lịch sử AUTO.");
                var events = await response.Content.ReadFromJsonAsync<AutoHistoryEvent[]>(cancellationToken: ct);
                if (events is null || events.Any(e => e is null || string.IsNullOrWhiteSpace(e.EventId) ||
                    e.Command is not ("ON" or "OFF") || (e.AutoType is not (null or "scheduled" or "full_time")) || !windows.Any(w => w.DeviceId == e.DeviceId &&
                        w.FeederId == e.FeederId && e.OccurredAt >= w.From && (w.Until == null || e.OccurredAt < w.Until))) ||
                    events.Select(e => e.EventId).Distinct(StringComparer.Ordinal).Count() != events.Length)
                    throw Unavailable("Bridge trả lịch sử AUTO không hợp lệ.");
                items.AddRange(events.Select(e => new IotCommandHistoryItem(
                    e.EventId, nodeId, e.FeederId, group.Key, e.DeviceId, "AUTO", e.Command, null,
                    "sensor", e.OccurredAt.UtcDateTime, e.ReceivedAt.UtcDateTime, IotCommandHistoryStatus.Success, e.AutoType ?? "scheduled")));
            }
            catch (Exception ex) when (IsSourceFailure(ex, ct))
            {
                throw Unavailable("Không đọc được lịch sử AUTO từ IoT Bridge.");
            }
        }
        return items;
    }
}
