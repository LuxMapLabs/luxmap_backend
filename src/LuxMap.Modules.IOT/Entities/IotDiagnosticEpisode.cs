using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.IOT.Entities;

// Feeder/node IDs are historical snapshots: detaching a node must retain these rows.
public sealed class IotDiagnosticEpisode : ICommuneScoped
{
    public long Id { get; set; }
    public required string CommuneId { get; set; }
    public required string FeederId { get; set; }
    public required string NodeId { get; set; }
    public required string Code { get; set; }
    public required string Signature { get; set; }
    public string Anomaly { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Status { get; set; } = "active";
}
public sealed class IotDiagnosticEpisodeConfiguration : IEntityTypeConfiguration<IotDiagnosticEpisode>
{
    public void Configure(EntityTypeBuilder<IotDiagnosticEpisode> b)
    {
        b.ToTable("iot_diagnostic_episode", t => {
            t.HasCheckConstraint("ck_iot_diagnostic_episode_time", "ended_at >= started_at AND (closed_at IS NULL OR closed_at >= ended_at)");
            t.HasCheckConstraint("ck_iot_diagnostic_episode_status", "status IN ('active', 'resolved', 'interrupted') AND ((status = 'active') = (closed_at IS NULL))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.Anomaly).HasColumnType("jsonb");
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasIndex(x => x.CommuneId);
        b.HasIndex(x => new { x.FeederId, x.StartedAt, x.Id });
        b.HasIndex(x => new { x.NodeId, x.Code }).IsUnique().HasFilter("closed_at IS NULL");
    }
}
