using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Telemetry.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Telemetry.Configurations;

/// <summary>
/// The device side of <c>fault.origin_node_id</c> (BE-26, ERD Q11) — configured here, beside <see cref="IotNode"/>, so Faults never
/// references Telemetry (the <c>SurveyPublicationConfigurations</c> shape for <c>origin_observation_id</c>).
/// </summary>
public sealed class FaultOriginNodeConfiguration : IEntityTypeConfiguration<Fault>
{
    /// <summary>One fault per silence of a device (D-5): the second writer of the same silence hits this and skips.</summary>
    public const string OneFaultPerSilence = "ux_fault_origin_node_id_detected_at";

    public void Configure(EntityTypeBuilder<Fault> builder)
    {
        builder.ToTable("fault", table => table.HasCheckConstraint("ck_fault_node_source",
            "origin_node_id IS NULL OR (source_channel = 'iot' AND pole_id IS NULL)"));
        builder.Property(fault => fault.OriginNodeId).HasColumnType("text");

        // Composite with commune_id (iot_node's alternate key): a device's fault lives in the device's commune.
        builder.HasOne<IotNode>().WithMany()
            .HasForeignKey(fault => new { fault.OriginNodeId, fault.CommuneId })
            .HasPrincipalKey(node => new { node.NodeId, node.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(fault => new { fault.OriginNodeId, fault.DetectedAt }, OneFaultPerSilence)
            .IsUnique().HasFilter("fault_type = 'node_offline'").HasDatabaseName(OneFaultPerSilence);
        // The partial index above leads with origin_node_id; the foreign key's own index must be declared or EF drops it (partial-index rule).
        builder.HasIndex(fault => new { fault.OriginNodeId, fault.CommuneId }, "ix_fault_origin_node_id_commune_id")
            .HasDatabaseName("ix_fault_origin_node_id_commune_id");
    }
}
