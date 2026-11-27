using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;

namespace LuxMap.Persistence;

public sealed class FeederRepairPeriod : ICommuneScoped, IAudited
{
    public long Id { get; set; }
    public required string CommuneId { get; set; }
    public required string WorkOrderId { get; set; }
    public required string FeederId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
