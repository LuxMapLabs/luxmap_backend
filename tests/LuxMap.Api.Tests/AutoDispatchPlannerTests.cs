using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Tests;

public class AutoDispatchPlannerTests
{
    private static readonly DateOnly Start = new(2026, 11, 14); // Saturday
    private static readonly DispatchEngineer[] People = [new("USR-9999", "A", "C"), new("USR-10000", "B", "C")];
    private static DispatchFault Fault(int i, string segment = "S", string? pole = null, Severity severity = Severity.Medium, double? score = null)
        => new($"F-{i:D4}", "C", segment, segment, pole ?? $"P-{i:D4}", i, severity, score, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    private static AutoDispatchPreview Plan(IEnumerable<DispatchFault> faults, DispatchEngineer[]? people = null,
        EngineerLoad[]? loads = null, AutoDispatchOptions? options = null)
        => AutoDispatchPlanner.Plan(Start, faults, people ?? People, loads ?? [], options ?? new());

    [Fact]
    public void Seven_faults_on_two_roads_get_two_engineers_on_the_same_night()
    {
        var p = Plan(Enumerable.Range(1, 4).Select(i => Fault(i, "Tuyến A"))
            .Concat(Enumerable.Range(5, 3).Select(i => Fault(i, "Tuyến B"))));
        Assert.Equal(7, p.FaultCount);
        Assert.Equal(2, p.OrderCount);
        Assert.Equal(["Kiểm tra 4 cột sự cố - Tuyến A", "Kiểm tra 3 cột sự cố - Tuyến B"], p.Proposals.Select(x => x.Title));
        Assert.Equal(["USR-9999", "USR-10000"], p.Proposals.Select(x => x.AssignedTo));
        Assert.All(p.Proposals, x => Assert.Equal(Start, x.ScheduledDate));
    }

    [Theory]
    [InlineData(8, 1, 8)]
    [InlineData(9, 2, 1)]
    [InlineData(16, 2, 8)]
    [InlineData(17, 3, 1)]
    public void Splitting_uses_road_order_and_distinct_poles(int count, int orders, int last)
    {
        var faults = Enumerable.Range(1, count).Reverse().Select(i => Fault(i)).Append(Fault(100, pole: "P-0008") with { Along = 8 });
        var p = Plan(faults);
        Assert.Equal(orders, p.OrderCount);
        Assert.Equal(last, p.Proposals[^1].PoleCount);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"P-{i:D4}"), p.Proposals[0].PoleIds);
        Assert.Contains("F-0008", p.Proposals[0].FaultIds);
        Assert.Contains("F-0100", p.Proposals[0].FaultIds);
        Assert.Equal(9, p.Proposals[0].FaultIds.Length);
        Assert.Equal(orders == 1 ? "Kiểm tra 8 cột sự cố - S" : $"Kiểm tra 8 cột sự cố - S (ca 1/{orders})", p.Proposals[0].Title);
    }

    [Theory]
    [InlineData("2026-11-12T10:00:00Z", 14)]
    [InlineData("2026-11-13T10:00:00Z", 15)]
    [InlineData("2026-11-13T04:59:00Z", 14)]
    public void Nights_plus_lead_days_include_both_weekend_days(string now, int day)
    {
        var start = new WorkOrderAgendaOptions().NightOf(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)).AddDays(2);
        var p = AutoDispatchPlanner.Plan(start, [Fault(1)], People, [], new());
        Assert.Equal(new DateOnly(2026, 11, day), p.StartDate);
        Assert.Equal(p.StartDate, p.Proposals[0].ScheduledDate);
    }

    [Fact]
    public void Three_engineers_times_two_slots_prioritizes_critical_then_score_and_delays_low()
    {
        var people = People.Append(new DispatchEngineer("USR-10001", "C", "C")).ToArray();
        var faults = Enumerable.Range(1, 7).Select(i => Fault(i, $"S{i}", severity: i == 7 ? Severity.Critical : i == 1 ? Severity.Low : Severity.Medium, score: i)).ToArray();
        var p = Plan(faults, people);
        Assert.Equal(6, p.Proposals.Count(x => x.ScheduledDate == Start));
        Assert.Equal("F-0007", p.Proposals[0].FaultIds.Single());
        Assert.Equal("F-0006", p.Proposals[1].FaultIds.Single());
        Assert.Equal("F-0001", p.Proposals[^1].FaultIds.Single());
        Assert.Equal(Start.AddDays(1), p.Proposals[^1].ScheduledDate);
    }

    [Fact]
    public void Weekly_then_daily_load_then_numeric_id_break_ties_and_sunday_belongs_to_previous_week()
    {
        var monday = new DateOnly(2026, 11, 16);
        Assert.Equal(new DateOnly(2026, 11, 9), EngineerLoadBoard.WeekStart(monday.AddDays(-1)));
        var board = new EngineerLoadBoard([new("USR-9999", monday.AddDays(-1)), new("USR-10000", monday)], 2);
        Assert.Equal("USR-9999", board.Select(People, monday).Engineer!.UserId);
        // Both now have one weekly slot; A has none on Tuesday, B has one.
        board = new EngineerLoadBoard([new("USR-9999", monday), new("USR-10000", monday.AddDays(1))], 2);
        Assert.Equal("USR-9999", board.Select(People, monday.AddDays(1)).Engineer!.UserId);
        board = new EngineerLoadBoard([], 2);
        Assert.Equal("USR-9999", board.Select(People, monday).Engineer!.UserId);
        board = new EngineerLoadBoard([new("USR-9999", monday), new("USR-9999", monday)], 2);
        Assert.Equal("USR-10000", board.Select(People, monday).Engineer!.UserId);
    }

    [Fact]
    public void Groups_stay_in_their_commune_and_missing_engineers_keep_the_proposal()
    {
        var p = Plan([Fault(1), Fault(2) with { CommuneId = "D" }, Fault(3) with { SegmentId = null }]);
        Assert.Equal(2, p.OrderCount);
        Assert.Equal(new DispatchSkipped("F-0003", "NO_SEGMENT"), Assert.Single(p.Skipped));
        var other = Assert.Single(p.Proposals, x => x.CommuneId == "D");
        Assert.Null(other.AssignedTo);
        Assert.Equal("NO_ELIGIBLE_ENGINEER", other.UnassignedReason);
        Assert.Equal(Start, other.ScheduledDate);
    }

    [Fact]
    public void Segment_faults_are_individual_units_after_poles_in_numeric_fault_order()
    {
        var p = Plan([Fault(1), Fault(9999) with { PoleId = null }, Fault(10000) with { PoleId = null }]);
        Assert.Equal(["F-0001", "F-9999", "F-10000"], Assert.Single(p.Proposals).FaultIds);
        Assert.Equal(3, p.Proposals[0].PoleCount);
    }

    [Fact]
    public void Cap_is_deterministic_and_search_has_a_reported_bound()
    {
        var faults = Enumerable.Range(1, 2001).Select(i => Fault(i)).ToArray();
        var p = Plan(faults);
        Assert.Equal(2000, p.FaultCount);
        Assert.Equal(new DispatchSkipped("F-2001", "LIMIT_EXCEEDED"), Assert.Single(p.Skipped));
        var loads = Enumerable.Range(0, EngineerLoadBoard.SearchDays).SelectMany(i => new[] {
            new EngineerLoad("USR-9999", Start.AddDays(i)), new EngineerLoad("USR-9999", Start.AddDays(i)) });
        var slot = new EngineerLoadBoard(loads, 2).Select([People[0]], Start);
        Assert.Null(slot.Engineer);
        Assert.Equal("SEARCH_LIMIT_EXCEEDED", slot.Reason);
    }

    [Fact]
    public void Priority_null_is_last_and_input_order_cannot_change_the_preview()
    {
        var faults = new[] { Fault(1, "S1"), Fault(2, "S2", score: 0), Fault(3, "S3", score: 10) };
        var first = Plan(faults);
        Assert.Equal(["F-0003", "F-0002", "F-0001"], first.Proposals.Select(p => p.FaultIds.Single()));
        var second = Plan(faults.Reverse(), People.Reverse().ToArray());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData(0, 2, 8)]
    [InlineData(2, 0, 8)]
    [InlineData(2, 2, 0)]
    public void Nonpositive_configuration_is_rejected(int lead, int quota, int max)
        => Assert.Throws<InvalidOperationException>(() => new AutoDispatchOptions { LeadDays = lead, QuotaPerDay = quota, MaxPolesPerOrder = max }.Validate());
}
