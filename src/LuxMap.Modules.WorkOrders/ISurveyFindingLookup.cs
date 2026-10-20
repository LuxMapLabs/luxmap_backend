using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

/// <summary>The fault type one pole's latest accepted survey result implies (out → lamp_out, dim → lamp_dim).</summary>
public sealed record SurveyFinding(string PoleId, FaultType FaultType, DataSource DataSource);

/// <summary>
/// Survey owns its published results and references WorkOrders, so WorkOrders asks through this port
/// (same shape as <see cref="IPolePhotoPublication"/>). Drift WSC-2.
/// </summary>
public interface ISurveyFindingLookup
{
    /// <summary>
    /// Per pole of <paramref name="communeId"/>, the latest result across the order's accepted sweeps; a pole
    /// whose latest result is neither out nor dim is absent, so an older out does not come back.
    /// </summary>
    Task<SurveyFinding[]> FindingsAsync(string workOrderId, string communeId, CancellationToken ct);
}
