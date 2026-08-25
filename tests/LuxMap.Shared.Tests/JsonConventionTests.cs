using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>Contract v1.1 mục 0 — snake_case, ngày YYYY-MM-DD, thời gian ISO 8601 UTC hậu tố Z.</summary>
public class JsonConventionTests
{
    private static readonly JsonSerializerOptions Options = LuxMapJsonOptions.Default;

    private sealed record PoleProperties(
        string PoleId,
        string SegmentId,
        FixtureStatus FixtureStatus,
        double? StatusConfidence,
        PowerSource PowerSource,
        DateOnly InstallDate,
        DateTime LastSeenAt,
        int OpenFaultCount,
        bool HasIotNode,
        bool NearSensitivePoi);

    private static PoleProperties Sample() => new(
        "POLE-0047",
        "SEG-003",
        FixtureStatus.Dim,
        0.82,
        PowerSource.Solar,
        new DateOnly(2023, 1, 4),
        new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc),
        2,
        HasIotNode: true,
        NearSensitivePoi: false);

    [Fact]
    public void Properties_serialize_as_snake_case()
    {
        var json = JsonSerializer.Serialize(Sample(), Options);

        Assert.Contains("\"pole_id\":\"POLE-0047\"", json);
        Assert.Contains("\"fixture_status\":\"dim\"", json);
        Assert.Contains("\"status_confidence\":0.82", json);
        Assert.Contains("\"open_fault_count\":2", json);
        Assert.Contains("\"has_iot_node\":true", json);
        Assert.Contains("\"near_sensitive_poi\":false", json);

        // Không có khoá camelCase nào lọt ra.
        Assert.DoesNotContain("poleId", json);
        Assert.DoesNotContain("nearSensitivePoi", json);
    }

    [Fact]
    public void Date_only_fields_serialize_as_yyyy_MM_dd()
    {
        var json = JsonSerializer.Serialize(Sample(), Options);
        Assert.Contains("\"install_date\":\"2023-01-04\"", json);
    }

    [Fact]
    public void Dictionary_keys_are_snake_cased()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, int> { ["PoleCount"] = 103 }, Options);
        Assert.Equal("{\"pole_count\":103}", json);
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var json = JsonSerializer.Serialize(Sample(), Options);
        var back = JsonSerializer.Deserialize<PoleProperties>(json, Options);

        Assert.Equal(Sample(), back);
    }
}
