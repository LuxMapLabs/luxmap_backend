using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

/// <summary>The fault type implied by one pole's accepted sweep and confirmed photo/clip observations (out → lamp_out, dim → lamp_dim).</summary>
public sealed record SurveyFinding(string PoleId, FaultType FaultType, DataSource DataSource);

/// <summary>
/// Survey owns its published results and references WorkOrders, so WorkOrders asks through this port
/// (same shape as <see cref="IPolePhotoPublication"/>). Drift WSC-2 / WSC-6.
/// </summary>
public interface ISurveyFindingLookup
{
    /// <summary>
    /// Per pole of <paramref name="communeId"/>, replay the order's accepted sweeps and confirmed photos/clips
    /// in observation order. ON preserves dim/normal; a final result other than out/dim is absent.
    /// </summary>
    Task<SurveyFinding[]> FindingsAsync(string workOrderId, string communeId, CancellationToken ct);
}
