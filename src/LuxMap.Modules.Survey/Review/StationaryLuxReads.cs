using System.Text.Json;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>The one mapping from a stored verdict to the wire (LUX-BASELINE D-13): lux readings, pole photos, the pole detail.</summary>
public static class LuxEvaluationReads
{
    public static async Task<Dictionary<string, LuxEvaluationResponse>> ForReadingsAsync(LuxMapDbContext db,
        IReadOnlyCollection<string> luxIds, CancellationToken ct)
    {
        if (luxIds.Count == 0) return [];
        var rows = await db.Set<LuxEvaluation>().AsNoTracking().Where(e => luxIds.Contains(e.LuxId)).ToListAsync(ct);
        var baselineIds = rows.Where(e => e.BaselineId is not null).Select(e => e.BaselineId!.Value).Distinct().ToArray();
        var counts = baselineIds.Length == 0 ? [] : await db.Set<StationaryBaseline>().AsNoTracking()
            .Where(b => baselineIds.Contains(b.BaselineId)).ToDictionaryAsync(b => b.BaselineId, b => b.MemberCount, ct);
        return rows.ToDictionary(e => e.LuxId, e => ToResponse(e, e.BaselineId is { } id ? counts.GetValueOrDefault(id) : null));
    }

    public static LuxEvaluationResponse ToResponse(LuxEvaluation e, int? memberCount)
        => new(ContractEnum.ToDbValue(e.ClassifiedAs), e.BaselineRatio, e.BaselineValue, memberCount, e.DimThresholdRatio,
            e.DimEvaluationEligible, e.ExpectedRatio, Reasons(e.ReasonCodes), e.EvaluatedAt);

    public static IReadOnlyList<string> Reasons(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
}
