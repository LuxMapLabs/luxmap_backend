using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.IOT;

public sealed record IotFeederAverage(
    DateTime PeriodStart, DateTime PeriodEnd, long SampleCount,
    double VoltageAvg, double CurrentMaAvg, double PowerMwAvg, bool IsComplete);
public sealed record IotFeederHistoryResult(
    string FeederId, string TimeBasis, int IntervalSeconds, int Page, int PageSize, int Total,
    IReadOnlyList<IotFeederAverage> Items);

public sealed partial class IotDiagnosticsService
{
    public async Task<IotFeederHistoryResult> FeederAveragedHistoryAsync(
        string feederId, PageRequest page, string timeBasis, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var start = from.HasValue ? new DateTimeOffset(UtcNormalization.ToUtc(from.Value)) : (DateTimeOffset?)null;
        var end = to.HasValue ? new DateTimeOffset(UtcNormalization.ToUtc(to.Value)) : (DateTimeOffset?)null;
        if (start.HasValue && end.HasValue && start >= end)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "from phải nhỏ hơn to.", new Dictionary<string, object?> { ["field"] = "to" });
        var bindings = await registry.FeederTelemetryBindingsAsync(feederId, ct);
        IotFeederHistoryResult Result(int total, IReadOnlyList<IotFeederAverage> items) =>
            new(feederId, timeBasis, 30, page.Page, page.PageSize, total, items);
        if (bindings.Count == 0) return Result(0, []);

        var settings = await registry.ReadAsync([], ct);
        Uri Origin(FeederTelemetryBinding binding)
        {
            var address = settings.Servers.TryGetValue(binding.ServerId, out var server) ? server.BaseUrl
                : binding.ServerId == "default" ? settings.BaseUrl : null;
            return IotOrigin.TryAllowed(settings, address, out var uri) ? uri
                : throw Unavailable("Không tìm thấy địa chỉ IoT Bridge của lịch sử mạch điện.");
        }
        var groups = bindings.GroupBy(Origin).OrderBy(group => group.Key.AbsoluteUri, StringComparer.Ordinal)
            .Select(group => (Origin: group.Key, Bindings: group.Distinct().ToArray())).ToArray();
        var offset = ((long)page.Page - 1) * page.PageSize;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var now = clock.GetUtcNow();
        IotFeederAverage Convert(IEnumerable<BridgeAverage> rows)
        {
            var array = rows.ToArray();
            var count = checked(array.Sum(row => row.SampleCount));
            var period = DateTimeOffset.FromUnixTimeSeconds(array[0].Period);
            return new(period.UtcDateTime, period.AddSeconds(30).UtcDateTime, count,
                array.Sum(row => row.VoltageAvg * (row.SampleCount / (double)count)),
                array.Sum(row => row.CurrentMaAvg * (row.SampleCount / (double)count)),
                array.Sum(row => row.PowerMwAvg * (row.SampleCount / (double)count)), period.AddSeconds(30) <= now);
        }
        try
        {
            if (groups.Length == 1)
            {
                var batch = await ReadFeederAveragesAsync(groups[0].Origin, groups[0].Bindings,
                    offset, page.PageSize, timeBasis, start, end, false, budget.Token);
                return Result(batch.Total, batch.Items.Select(row => Convert([row])).ToArray());
            }

            // Fetch compact bucket keys, never raw samples. Distinct keys give the union total
            // when a feeder changes Bridge inside a 30-second period.
            var indexes = await Task.WhenAll(groups.Select(group => ReadFeederAveragesAsync(group.Origin,
                group.Bindings, 0, page.PageSize, timeBasis, start, end, true, budget.Token)));
            var periods = indexes.SelectMany(index => index.Periods).Distinct().OrderDescending().ToArray();
            var selected = periods.Skip((int)Math.Min(offset, periods.Length)).Take(page.PageSize).ToArray();
            if (selected.Length == 0) return Result(periods.Length, []);
            var pageStart = DateTimeOffset.FromUnixTimeSeconds(selected[^1]);
            var pageEnd = DateTimeOffset.FromUnixTimeSeconds(selected[0]).AddSeconds(30);
            if (start is { } lower && lower > pageStart) pageStart = lower;
            if (end is { } upper && upper < pageEnd) pageEnd = upper;
            var batches = await Task.WhenAll(groups.Select(group => ReadFeederAveragesAsync(group.Origin,
                group.Bindings, 0, page.PageSize, timeBasis, pageStart, pageEnd, false, budget.Token)));
            var items = batches.SelectMany(batch => batch.Items).GroupBy(row => row.Period)
                .OrderByDescending(group => group.Key).Select(group => Convert(group)).ToArray();
            return Result(periods.Length, items);
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không đọc được lịch sử điện trung bình từ IoT Bridge.");
        }
    }

    private async Task<BridgeAverageBatch> ReadFeederAveragesAsync(
        Uri origin, FeederTelemetryBinding[] bindings, long skip, int take, string timeBasis,
        DateTimeOffset? from, DateTimeOffset? to, bool keysOnly, CancellationToken ct)
    {
        var path = FormattableString.Invariant(
            $"api/luxmap/devices/feeder-history/averages?skip={skip}&take={take}&timeBasis={timeBasis}&keysOnly={keysOnly}");
        if (from is { } start) path += "&from=" + Uri.EscapeDataString(start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        if (to is { } end) path += "&to=" + Uri.EscapeDataString(end.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        using var response = await client.PostAsJsonAsync(new Uri(origin, path), bindings, ct);
        response.EnsureSuccessStatusCode();
        if (!IsJson(response)) throw Unavailable("IoT Bridge không trả JSON cho lịch sử mạch điện.");
        var batch = await response.Content.ReadFromJsonAsync<BridgeAverageBatch>(cancellationToken: ct);
        if (batch is not { Items: not null, Periods: not null, Total: >= 0 } ||
            batch.Total != batch.Periods.Count || batch.Periods.Distinct().Count() != batch.Total ||
            batch.Periods.Any(period => period < 0 || period > 253402300740 || period % 30 != 0) ||
            !batch.Periods.SequenceEqual(batch.Periods.OrderDescending()) ||
            batch.Items.Any(row => row is null) ||
            batch.Items.Count != (keysOnly ? 0 : Math.Min(take, Math.Max(0, batch.Total - skip))) ||
            !batch.Items.Select(row => row.Period).SequenceEqual(keysOnly ? [] : batch.Periods.Skip((int)Math.Min(skip, batch.Total)).Take(take)) ||
            batch.Items.Any(row => row.SampleCount <= 0 || !double.IsFinite(row.VoltageAvg) ||
                !double.IsFinite(row.CurrentMaAvg) || !double.IsFinite(row.PowerMwAvg)))
            throw Unavailable("IoT Bridge trả lịch sử điện trung bình không hợp lệ.");
        return batch;
    }

    private sealed record BridgeAverage(long Period, long SampleCount, double VoltageAvg, double CurrentMaAvg, double PowerMwAvg);
    private sealed record BridgeAverageBatch(int Total, IReadOnlyList<long> Periods, IReadOnlyList<BridgeAverage> Items);
}
