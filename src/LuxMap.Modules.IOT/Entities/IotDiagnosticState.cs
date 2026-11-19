using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.IOT.Entities;

public sealed class IotDiagnosticState : ICommuneScoped
{
    public required string NodeId { get; set; }
    public required string CommuneId { get; set; }
    public string? FeederId { get; set; }
    public string DiagnosticStatus { get; set; } = "no_data";
    public string Result { get; set; } = "{}";
    public string Evidence { get; set; } = "{}";
    public DateTime EvaluatedAt { get; set; }
    public DateTime? HistorySampledAt { get; set; }
}

public sealed class IotDiagnosticStateConfiguration : IEntityTypeConfiguration<IotDiagnosticState>
{
    public void Configure(EntityTypeBuilder<IotDiagnosticState> b)
    {
        b.ToTable("iot_diagnostic_state");
        b.HasKey(x => x.NodeId);
        b.Property(x => x.Result).HasColumnType("jsonb");
        b.Property(x => x.Evidence).HasColumnType("jsonb");
        b.HasIndex(x => x.CommuneId);
        b.HasCommuneScope();
        b.HasCommuneReference(x => x.CommuneId);
        b.HasOne<IotNode>().WithMany().HasForeignKey(x => new { x.NodeId, x.CommuneId })
            .HasPrincipalKey(x => new { x.NodeId, x.CommuneId }).OnDelete(DeleteBehavior.Cascade);
    }
}
