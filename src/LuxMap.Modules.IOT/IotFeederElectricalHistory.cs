using System.Net;
using System.Net.Http.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.IOT;

public sealed record IotFeederElectricalResult(
    string FeederId, string? NodeId, string? ServerId, string Status, IotMeasurement? Measurement,
    FeederPowerState PowerState = FeederPowerState.Unknown);

public sealed record IotFeederArchivedSample(long Cursor, IotArchivedReading Reading, string NodeId, string ServerId)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Active { get; init; }
}

public sealed partial class IotDiagnosticsService
{
    public async Task<IotFeederElectricalResult> FeederElectricalAsync(string feederId, CancellationToken ct)
    {
        var history = await FeederTelemetryHistoryAsync(feederId, PageRequest.Create(1, 1), "measured_at", ct);
        var (_, links) = await sources.LinksAsync(db, [feederId], ct);
        links.TryGetValue(feederId, out var link);
        var sample = history.Items.FirstOrDefault();
        if (sample is null)
            return new(feederId, link?.NodeId, link?.Profile.ServerId, link is null ? "unconfigured" : "no_data", null);

        var reading = sample.Reading;
        var data = reading.Data;
        if (data.LampCommand is not ("ON" or "OFF") ||
            !Valid(data.Voltage) || !Valid(data.Current) || !Valid(data.Power))
            throw Unavailable("Mẫu điện của mạch không hợp lệ.");

        var measurement = new IotMeasurement(reading.ReceivedAt.UtcDateTime, data.LampCommand,
            data.Voltage!.Value, data.Current!.Value, data.Power!.Value);
        var measuredAt = MeasuredAt(data.Timestamp) ?? reading.ReceivedAt;
        var now = clock.GetUtcNow();
        var active = sample.Active && link is not null && link.NodeId == sample.NodeId &&
            (link.Profile.ServerId ?? "default") == sample.ServerId;
        var fresh = active && measuredAt <= now && now - measuredAt <= TimeSpan.FromSeconds(link!.Profile.MaxSilenceSeconds);
        return new(feederId, sample.NodeId, sample.ServerId, fresh ? "online" : "offline", measurement,
            fresh ? DecodePower(measurement.LampCommand) : FeederPowerState.Unknown);
    }

    private async Task<PagedResult<IotFeederArchivedSample>> FeederTelemetryHistoryAsync(
        string feederId, PageRequest page, string sortBy, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (sortBy is not ("received_at" or "measured_at"))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "sort_by phải là received_at hoặc measured_at.");

        var bindings = await registry.FeederTelemetryBindingsAsync(feederId, ct);
        if (bindings.Count == 0) return PagedResult<IotFeederArchivedSample>.Empty(page);

        var settings = await registry.ReadAsync([], ct);
        Uri ResolveOrigin(FeederTelemetryBinding binding)
        {
            var address = settings.Servers.TryGetValue(binding.ServerId, out var server) ? server.BaseUrl
                : binding.ServerId == "default" ? settings.BaseUrl : null;
            return IotOrigin.TryAllowed(settings, address, out var uri) ? uri
                : throw Unavailable("Không tìm thấy địa chỉ IoT Bridge của lịch sử mạch điện.");
        }

        var groups = bindings.GroupBy(ResolveOrigin).OrderBy(group => group.Key.AbsoluteUri, StringComparer.Ordinal)
            .Select(group => new FeederBridgePage(group.Key, group.ToArray(), from, to)).ToArray();
        var offset = ((long)page.Page - 1) * page.PageSize;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (groups.Length == 1)
            {
                var batch = await ReadFeederBatchAsync(groups[0], offset, page.PageSize, sortBy, budget.Token);
                return PagedResult<IotFeederArchivedSample>.From(page, batch.Total, ToFeederItems(batch.Items));
            }

            // Each origin returns sorted chunks. Merge them so page offsets work across all Bridges.
            const int chunkSize = PageRequest.MaxPageSize;
            var first = await Task.WhenAll(groups.Select(group => ReadFeederBatchAsync(group, 0, chunkSize, sortBy, budget.Token)));
            var total = first.Sum(batch => batch.Total);
            if (offset >= total) return PagedResult<IotFeederArchivedSample>.From(page, total, []);

            for (var index = 0; index < groups.Length; index++) groups[index].Batch = first[index];
            var items = new List<BridgeFeederSample>(page.PageSize);
            var limit = Math.Min(total, offset + page.PageSize);
            for (long position = 0; position < limit; position++)
            {
                FeederBridgePage? best = null;
                foreach (var group in groups)
                {
                    if (group.Consumed == group.Batch.Items.Count)
                    {
                        group.Offset += group.Consumed;
                        group.Consumed = 0;
                        if (group.Offset >= group.Batch.Total)
                        {
                            group.Batch = new(group.Batch.Total, []);
                            continue;
                        }

                        group.Batch = await ReadFeederBatchAsync(group, group.Offset, chunkSize, sortBy, budget.Token);
                    }

                    if (group.Batch.Items.Count > 0 &&
                        (best is null || CompareFeederTime(group.Current, best.Current, sortBy) > 0))
                        best = group;
                }
                if (best is null) break;
                if (position >= offset) items.Add(best.Current);
                best.Consumed++;
            }

            return PagedResult<IotFeederArchivedSample>.From(page, total, ToFeederItems(items));
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không đọc được lịch sử điện của mạch từ IoT Bridge.");
        }
    }

    private async Task<FeederArchiveBatch> ReadFeederBatchAsync(
        FeederBridgePage group, long offset, int take, string sortBy, CancellationToken ct)
    {
        var path = FormattableString.Invariant($"api/luxmap/devices/feeder-history?skip={offset}&take={take}&sortBy={sortBy}");
        if (group.From is { } start) path += "&from=" + Uri.EscapeDataString(start.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        if (group.To is { } end) path += "&to=" + Uri.EscapeDataString(end.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        using var response = await client.PostAsJsonAsync(new Uri(group.Origin, path), group.Bindings, ct);
        response.EnsureSuccessStatusCode();
        if (!IsJson(response)) throw Unavailable("IoT Bridge không trả JSON cho lịch sử mạch điện.");
        var batch = await response.Content.ReadFromJsonAsync<FeederArchiveBatch>(cancellationToken: ct);
        if (batch is not { Items: not null, Total: >= 0 } || batch.Items.Count != Math.Min(take, Math.Max(0, batch.Total - offset)) ||
            batch.Items.Any(sample => sample is null || sample.Reading?.Data is null ||
                !group.Bindings.Any(binding => binding.NodeId == sample.NodeId && binding.ServerId == sample.ServerId)))
            throw Unavailable("IoT Bridge trả trang lịch sử mạch điện không hợp lệ.");
        return batch;
    }

    private static int CompareFeederTime(BridgeFeederSample left, BridgeFeederSample right, string sortBy) =>
        sortBy == "measured_at" ? string.CompareOrdinal(left.Reading.Data.Timestamp, right.Reading.Data.Timestamp)
            : left.Reading.ReceivedAt.CompareTo(right.Reading.ReceivedAt);

    private static IotFeederArchivedSample[] ToFeederItems(IEnumerable<BridgeFeederSample> samples) =>
        [.. samples.Select(sample => new IotFeederArchivedSample(sample.Cursor, sample.Reading, sample.NodeId, sample.ServerId)
            { Active = sample.Active })];

    private sealed record BridgeFeederSample(long Cursor, IotArchivedReading Reading, string NodeId, string ServerId, bool Active);
    private sealed record FeederArchiveBatch(int Total, IReadOnlyList<BridgeFeederSample> Items);

    private sealed class FeederBridgePage(Uri origin, FeederTelemetryBinding[] bindings, DateTimeOffset? from, DateTimeOffset? to)
    {
        public DateTimeOffset? From { get; } = from;
        public DateTimeOffset? To { get; } = to;
        public Uri Origin { get; } = origin;
        public FeederTelemetryBinding[] Bindings { get; } = bindings;
        public FeederArchiveBatch Batch { get; set; } = new(0, []);
        public long Offset { get; set; }
        public int Consumed { get; set; }
        public BridgeFeederSample Current => Batch.Items[Consumed];
    }
}
