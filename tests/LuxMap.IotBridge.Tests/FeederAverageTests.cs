using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LuxMap.IotBridge.Tests;

public sealed class FeederAverageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luxmap-averages-" + Guid.NewGuid().ToString("N"));
    private readonly TelemetryArchive archive;
    private readonly DateTimeOffset start = DateTimeOffset.Parse("2026-11-18T07:00:00Z", CultureInfo.InvariantCulture);
    private long sequence;

    public FeederAverageTests()
    {
        Directory.CreateDirectory(directory);
        archive = new(directory, []);
    }

    private FeederTelemetryWindow Window(string device = "probe", DateTimeOffset? from = null, DateTimeOffset? until = null) =>
        new("NODE-test", "server-test", device, from ?? start.AddDays(-1), until);

    private void Save(int second, double voltage = 4, double current = 10, double power = 40,
        int? receipt = null, bool missingRtc = false, string device = "probe")
    {
        var timestamp = missingRtc ? null : start.AddSeconds(second).ToOffset(TimeSpan.FromHours(7))
            .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        archive.Save(device, new(new(device, "MANUAL", "ON", 10, voltage, current, power, timestamp,
            "test-session", ++sequence), start.AddSeconds(receipt ?? second)));
    }

    [Fact]
    public void Groups_at_exact_30_second_boundaries_averages_independently_and_paginates_buckets()
    {
        Save(0, 4, 10, 40); Save(29, 6, 20, 120);
        Save(30, 5, 100, 500); Save(59, 5, 200, 1000); Save(60);
        var page = archive.ReadFeederAverages([Window()], 1, 1, true, null, null);
        Assert.Equal(3, page.Total);
        var bucket = Assert.Single(page.Items);
        Assert.Equal(start.AddSeconds(30).ToUnixTimeSeconds(), bucket.Period);
        Assert.Equal(2, bucket.SampleCount);
        Assert.Equal(5, bucket.VoltageAvg);
        Assert.Equal(150, bucket.CurrentMaAvg);
        Assert.Equal(750, bucket.PowerMwAvg);
        var oldest = Assert.Single(archive.ReadFeederAverages([Window()], 2, 1, true, null, null).Items);
        Assert.Equal(80, oldest.PowerMwAvg); // Average power, not product of average V and I.
        Assert.Equal(5, oldest.VoltageAvg);
        Assert.Equal(15, oldest.CurrentMaAvg);
        Assert.Empty(archive.ReadFeederAverages([Window()], 3, 1, true, null, null).Items);
    }

    [Fact]
    public void Filters_half_open_bounds_before_averaging_and_does_not_fill_empty_buckets()
    {
        Save(0, current: 100); Save(10, current: 10); Save(20, current: 20); Save(30, current: 200); Save(120);
        var page = archive.ReadFeederAverages([Window()], 0, 20, true, start.AddSeconds(10), start.AddSeconds(30));
        Assert.Equal(1, page.Total);
        Assert.Equal(15, Assert.Single(page.Items).CurrentMaAvg);
        Assert.Equal(2, page.Items[0].SampleCount);
        Assert.Equal(3, archive.ReadFeederAverages([Window()], 0, 20, true, null, null).Total);
        Assert.Empty(archive.ReadFeederAverages([Window()], 0, 20, true, start.AddSeconds(60), start.AddSeconds(90)).Items);
    }

    [Fact]
    public void Replay_uses_measurement_time_or_receipt_time_and_missing_rtc_only_has_receipt_bucket()
    {
        Save(0, receipt: 3600); Save(30, receipt: 3601); Save(60, receipt: 3602, missingRtc: true);
        var windows = new[] { Window(until: start.AddSeconds(60)) };
        var measured = archive.ReadFeederAverages(windows, 0, 20, true, null, null);
        Assert.Equal(2, measured.Total);
        Assert.Equal(start.AddSeconds(30).ToUnixTimeSeconds(), measured.Items[0].Period);
        // Missing RTC is assigned by receipt and is outside the closed feeder assignment.
        var received = archive.ReadFeederAverages(windows, 0, 20, false, start.AddHours(1), start.AddHours(2));
        Assert.Equal(2, Assert.Single(received.Items).SampleCount);
        var active = archive.ReadFeederAverages([Window()], 0, 20, false, null, null);
        Assert.Equal(3, Assert.Single(active.Items).SampleCount);
        Assert.Equal(2, archive.ReadFeederAverages([Window()], 0, 20, true, null, null).Total);
    }

    [Fact]
    public void Replacement_and_overlapping_assignments_do_not_count_a_sample_twice()
    {
        Save(0); Save(10); Save(20, current: 30, device: "replacement");
        var windows = new[] { Window(until: start.AddSeconds(15)), Window(from: start.AddSeconds(-2)),
            Window("replacement", start.AddSeconds(15)) };
        var page = archive.ReadFeederAverages(windows, 0, 20, true, null, null);
        Assert.Equal(3, Assert.Single(page.Items).SampleCount);
        Assert.Equal(50.0 / 3, page.Items[0].CurrentMaAvg, 6);
        var keys = archive.ReadFeederAverages(windows, 0, 20, true, null, null, keysOnly: true);
        Assert.Equal(page.Periods, keys.Periods);
        Assert.Empty(keys.Items);
    }

    [Fact]
    public void Duplicate_delivery_is_idempotent_and_invalid_electrical_sample_is_excluded()
    {
        var data = new TelemetryRequest("probe", "MANUAL", "OFF", 0, 0, 0, 0,
            "2026-11-18T14:00:00", "duplicate", 1);
        archive.Save("probe", new(data, start));
        archive.Save("probe", new(data, start.AddSeconds(30)));
        archive.Save("probe", new(data with { SequenceNumber = 2, Current = null }, start));
        var page = archive.ReadFeederAverages([Window()], 0, 20, false, null, null);
        Assert.Equal(1, Assert.Single(page.Items).SampleCount);
        Assert.Equal(0, page.Items[0].CurrentMaAvg);
        Assert.Equal(2, archive.ReadPage("probe", 0, 20).Items.Count); // Raw records are retained.
    }

    [Fact]
    public void Receipt_filters_preserve_fractional_seconds_at_bucket_and_query_boundaries()
    {
        var data = new TelemetryRequest("probe", "MANUAL", "ON", 10, 5, 10, 50,
            "2026-11-18T14:00:00", "fraction", 1);
        var justBefore = start.AddSeconds(30).AddTicks(-1);
        archive.Save("probe", new(data, justBefore));
        archive.Save("probe", new(data with { SequenceNumber = 2, Current = 20 }, start.AddSeconds(30)));
        var page = archive.ReadFeederAverages([Window()], 0, 20, false, justBefore, start.AddSeconds(30));
        var item = Assert.Single(page.Items);
        Assert.Equal(start.ToUnixTimeSeconds(), item.Period);
        Assert.Equal(1, item.SampleCount);
        Assert.Equal(10, item.CurrentMaAvg);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}
