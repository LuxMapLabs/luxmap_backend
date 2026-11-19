namespace LuxMap.Modules.WorkOrders;

public sealed record RepairStatusOutcome(string PoleId, bool Applied, string Reason);

/// <summary>Stages publication only; caller holds work-order → ordinal pole locks and saves in the same transaction.</summary>
public interface IRepairStatusPublication
{
    Task<IReadOnlyList<RepairStatusOutcome>> StageAsync(IReadOnlyCollection<string> poleIds,
        IReadOnlyCollection<string> verifiedFaultIds, DateTime observedAt, DateTime now, CancellationToken ct);
}
