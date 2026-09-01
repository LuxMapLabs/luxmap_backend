using System.Text.Json;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Persistence.Tests;

/// <summary>
/// The BE-03 decision: enums are stored as <c>text</c> holding exactly the Contract string.
/// The database value and the JSON value must match EXACTLY — one character of drift and the manual
/// reporting queries (CV-11, CV-18, IOT-16) return different numbers from the API.
/// </summary>
public class ContractEnumStorageTests
{
    private static readonly JsonSerializerOptions Json = LuxMapJsonOptions.Default;

    private static IEnumerable<object[]> AllEnumTypes() =>
    [
        [typeof(FixtureStatus)], [typeof(PowerSource)], [typeof(FixtureType)],
        [typeof(FaultType)], [typeof(FaultStatus)], [typeof(Severity)],
        [typeof(SourceChannel)], [typeof(DataSource)], [typeof(WorkOrderStatus)],
        [typeof(NodeRole)], [typeof(NodeStatus)], [typeof(RoadClass)],
    ];

    public static TheoryData<Type> EnumTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var row in AllEnumTypes())
        {
            data.Add((Type)row[0]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EnumTypes))]
    public void Db_value_matches_json_value_for_every_member(Type enumType)
    {
        foreach (var member in Enum.GetValues(enumType))
        {
            var json = JsonSerializer.Serialize(member, enumType, Json).Trim('"');
            var db = (string)typeof(ContractEnum)
                .GetMethod(nameof(ContractEnum.ToDbValue))!
                .MakeGenericMethod(enumType)
                .Invoke(null, [member])!;

            Assert.Equal(json, db);
        }
    }

    [Fact]
    public void Db_values_are_contract_strings_not_csharp_names()
    {
        Assert.Equal("lamp_out", ContractEnum.ToDbValue(FaultType.LampOut));
        Assert.Equal("field_report", ContractEnum.ToDbValue(SourceChannel.FieldReport));
        Assert.Equal("calibration_rig", ContractEnum.ToDbValue(DataSource.CalibrationRig));
        Assert.Equal("never_reported", ContractEnum.ToDbValue(NodeStatus.NeverReported));
    }

    [Fact]
    public void Converter_round_trips_every_value()
    {
        var converter = ContractEnum.Converter<FaultStatus>();

        foreach (var value in Enum.GetValues<FaultStatus>())
        {
            var stored = (string)converter.ConvertToProvider(value)!;
            Assert.Equal(value, (FaultStatus)converter.ConvertFromProvider(stored)!);
        }
    }

    [Fact]
    public void Check_constraint_lists_every_allowed_value()
    {
        Assert.Equal(
            ["detected", "confirmed", "rejected", "in_progress", "resolved", "verified"],
            ContractEnum.AllDbValues<FaultStatus>());
    }
}
