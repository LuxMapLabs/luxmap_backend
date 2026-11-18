using LuxMap.Modules.Faults.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.IOT.Configurations;

public sealed class FaultDiagnosticConfiguration : IEntityTypeConfiguration<Fault>
{
    public void Configure(EntityTypeBuilder<Fault> b)
    {
        b.HasIndex(f => new { f.CommuneId, f.OriginNodeId, f.FeederId, f.IotAnomalyCode })
            .HasDatabaseName("ux_fault_iot_diagnostic_active").IsUnique()
            .HasFilter("iot_anomaly_code IS NOT NULL AND fault_status IN ('detected','confirmed','in_progress','resolved')");
        b.HasIndex(f => f.CommuneId);
    }
}
