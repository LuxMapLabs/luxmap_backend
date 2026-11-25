using System.Globalization;

namespace LuxMap.IotBridge.Tests;

public sealed class FeederRawHistoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luxmap-raw-history-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset start = DateTimeOffset.Parse("2026-11-25T07:00:00Z", CultureInfo.InvariantCulture);
    private readonly TelemetryArchive archive;
    public FeederRawHistoryTests() { Directory.CreateDirectory(directory); archive = new(directory, []); }
    private FeederTelemetryWindow Window() => new("NODE-test", "server", "probe", start.AddDays(-1), null);
    private TelemetryReading Sample(int second, int receipt, string? type) => new(new("probe", "MANUAL", "ON",
        10, 5, 90 + second, 450 + second * 5, start.AddSeconds(second).ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-ddTHH:mm:ss"),
        "session", second + 1, ReceptionType: type), start.AddSeconds(receipt));

    [Fact]
    public void Raw_history_keeps_each_second_and_filters_before_pagination_in_both_time_bases()
    {
        for (var second = 0; second < 32; second++) archive.Save("probe", Sample(second, 100 - second, second % 2 == 0 ? "live" : "backfill"));
        var measured = archive.ReadFeederHistory([Window()], 0, 2, true);
        Assert.Equal(32, measured.Total);
        Assert.Equal(32L, measured.Items[0].Reading.Data.SequenceNumber);
        Assert.Equal(31L, measured.Items[1].Reading.Data.SequenceNumber);
        var received = archive.ReadFeederHistory([Window()], 0, 2, false);
        Assert.Equal(1L, received.Items[0].Reading.Data.SequenceNumber);
        Assert.Equal("live", received.Items[0].Reading.Data.ReceptionType);
        var subset = archive.ReadFeederHistory([Window()], 1, 1, true, start.AddSeconds(2), start.AddSeconds(5));
        Assert.Equal(3, subset.Total);
        Assert.Equal(4L, Assert.Single(subset.Items).Reading.Data.SequenceNumber);
        var receiptSubset = archive.ReadFeederHistory([Window()], 0, 20, false, start.AddSeconds(99), start.AddSeconds(101));
        Assert.Equal(2, receiptSubset.Total);
        Assert.Empty(archive.ReadFeederHistory([Window()], 32, 20, true).Items);
    }

    [Fact]
    public void Duplicate_replay_keeps_original_receipt_type_and_time_across_restart()
    {
        var first = Sample(0, 1, "live");
        archive.Save("probe", first);
        var duplicate = first with { Data = first.Data with { ReceptionType = "backfill" }, ReceivedAt = start.AddHours(1) };
        Assert.Equal(first, archive.Save("probe", duplicate));
        var reopened = new TelemetryArchive(directory, []);
        var row = Assert.Single(reopened.ReadFeederHistory([Window()], 0, 20, false).Items);
        Assert.Equal(first, row.Reading);
        Assert.Equal(1, reopened.ReadFeederHistory([Window()], 0, 20, false).Total);
    }

    [Fact]
    public void Legacy_type_stays_null_and_invalid_type_is_rejected()
    {
        var legacy = Sample(0, 0, null);
        Assert.Null(DeviceRules.ValidateTelemetry("probe", legacy.Data));
        archive.Save("probe", legacy);
        Assert.Null(Assert.Single(archive.ReadFeederHistory([Window()], 0, 20, true).Items).Reading.Data.ReceptionType);
        Assert.NotNull(DeviceRules.ValidateTelemetry("probe", legacy.Data with { ReceptionType = "guessed" }));
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
