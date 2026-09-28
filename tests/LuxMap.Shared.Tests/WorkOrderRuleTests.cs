using System.Text.Json;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Tests;

public class WorkOrderStateMachineTests
{
    public static readonly string[] Actions = ["assign", "reassign", "unassign", "edit", "start", "complete", "verify", "return", "cancel"];
    // Independent literal table, in Actions order. Six states, nine actions.
    public static readonly Dictionary<WorkOrderStatus, bool[]> Expected = new()
    {
        [WorkOrderStatus.Open] = [true, true, true, true, false, false, false, false, true],
        [WorkOrderStatus.Assigned] = [true, true, true, true, true, false, false, false, true],
        [WorkOrderStatus.InProgress] = [true, true, true, true, false, true, false, false, true],
        [WorkOrderStatus.Done] = [false, false, false, false, false, false, true, true, false],
        [WorkOrderStatus.Verified] = [false, false, false, false, false, false, false, false, false],
        [WorkOrderStatus.Cancelled] = [false, false, false, false, false, false, false, false, false],
    };

    [Fact]
    public void Literal_six_by_nine_transition_table()
    {
        foreach (var (state, expected) in Expected)
            for (var i = 0; i < Actions.Length; i++)
                Assert.True(expected[i] == WorkOrderRules.Allows(state, Actions[i]), $"{state}/{Actions[i]}");
    }
}

public class FaultEligibilityTests
{
    [Theory]
    [InlineData(FaultStatus.Detected, true, false)]
    [InlineData(FaultStatus.Confirmed, true, true)]
    [InlineData(FaultStatus.InProgress, true, true)]
    [InlineData(FaultStatus.Rejected, false, false)]
    [InlineData(FaultStatus.Resolved, false, false)]
    [InlineData(FaultStatus.Verified, false, false)]
    public void Literal_eligibility(FaultStatus status, bool inspection, bool repair)
    {
        Assert.Equal(inspection, WorkOrderRules.Eligible(TaskKind.Inspection, status));
        Assert.Equal(repair, WorkOrderRules.Eligible(TaskKind.Repair, status));
    }
}

public class MockWorkOrderKindsTests
{
    [Fact]
    public void Every_mock_order_has_a_kind_and_eligible_faults()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "mocks"))) root = root.Parent;
        var mocks = Path.Combine(root!.FullName, "mocks");
        var kinds = File.ReadAllLines(Path.Combine(mocks, "mock-work-order-kinds.csv")).Skip(1)
            .Select(x => x.Split(',')).ToDictionary(x => x[0], x => x[1]);
        using var orders = JsonDocument.Parse(File.ReadAllText(Path.Combine(mocks, "mock-work-orders.json")));
        using var faults = JsonDocument.Parse(File.ReadAllText(Path.Combine(mocks, "mock-faults.json")));
        Assert.Equal(orders.RootElement.GetProperty("items").GetArrayLength(), kinds.Count);
        foreach (var wo in orders.RootElement.GetProperty("items").EnumerateArray())
        {
            var kind = kinds[wo.GetProperty("work_order_id").GetString()!];
            Assert.Contains(kind, new[] { "inspection", "repair" });
            foreach (var id in wo.GetProperty("fault_ids").EnumerateArray())
            {
                var fault = faults.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("fault_id").GetString() == id.GetString());
                var status = fault.GetProperty("fault_status").GetString();
                Assert.True(status is "confirmed" or "in_progress" || (kind == "inspection" && status == "detected"));
            }
        }
    }
}
