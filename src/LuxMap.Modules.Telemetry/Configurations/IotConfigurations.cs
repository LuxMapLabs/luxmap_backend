using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Telemetry.Configurations;

public sealed class IotNodeConfiguration : IEntityTypeConfiguration<IotNode>
{
    public void Configure(EntityTypeBuilder<IotNode> builder)
    {
        builder.ToTable("iot_node", table =>
            // Stricter than the data_source enum on purpose: the team installs NO device in the field
            // (D-R10), so a device is testbed hardware (calibration_rig) or demo data (simulated),
            // never `field` — mixing a controlled rig into field figures is the error the
            // data_source split exists to prevent (I-5).
            table.HasCheckConstraint("ck_iot_node_data_source_not_field",
                "data_source IN ('calibration_rig', 'simulated')"));
        builder.HasKey(node => node.NodeId);

        builder.Property(node => node.NodeId).HasPrefixedId(PrefixedIds.IotNode);
        builder.Property(node => node.CabinetId).HasColumnType("text").IsRequired();
        builder.Property(node => node.CredentialHash).HasColumnType("text");

        // LC-2: a secret and its issue time come and go together.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_iot_node_credential_set_together", "(credential_hash IS NULL) = (credential_set_at IS NULL)"));
        builder.Property(node => node.SupportsRemoteControl).HasDefaultValue(false);
        builder.Property(node => node.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(node => node.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasContractEnum(node => node.NodeRole);
        builder.HasContractEnum(node => node.DataSource);

        builder.HasCommuneScope();

        // CAB-3: the device takes its point from its cabinet, and a cabinet carries at most one device.
        // The bbox of GET /map/iot-nodes now rides ix_electrical_cabinet_geom.
        builder.HasOne<ElectricalCabinet>().WithMany()
            .HasForeignKey(node => new { node.CabinetId, node.CommuneId })
            .HasPrincipalKey(cabinet => new { cabinet.CabinetId, cabinet.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(node => node.CabinetId).IsUnique().HasDatabaseName("ux_iot_node_cabinet_id");

        // Target of feeder_control's composite key, the same load-bearing "redundant" key as
        // ak_feeder_feeder_id_commune_id (O-7 trap 2). Do not drop it as dead weight.
        builder.HasAlternateKey(node => new { node.NodeId, node.CommuneId });

        builder.HasCommuneReference(node => node.CommuneId);
    }
}

public sealed class FeederControlConfiguration : IEntityTypeConfiguration<FeederControl>
{
    public void Configure(EntityTypeBuilder<FeederControl> builder)
    {
        builder.ToTable("feeder_control", table =>
        {
            table.HasCheckConstraint("ck_feeder_control_relay_no_positive", "relay_no > 0");
            table.HasCheckConstraint("ck_feeder_control_mode_reported",
                "(control_mode IS NULL) = (mode_reported_at IS NULL)");
        });

        // One device per feeder at most: the feeder IS the key (I-12).
        builder.HasKey(control => control.FeederId);

        builder.Property(control => control.FeederId).HasColumnType("text");
        builder.Property(control => control.NodeId).HasColumnType("text").IsRequired();

        // CAB-4: the database-only keys fk_feeder_control_feeder_same_cabinet and
        // fk_feeder_control_node_same_cabinet include it (raw SQL — see CabinetConstraints).
        builder.Property(control => control.CabinetId).HasColumnType("text").IsRequired();
        builder.HasContractEnum(control => control.ControlMode);

        builder.HasCommuneScope();

        // Both keys carry commune_id, so a device can only switch a feeder of its own commune.
        builder.HasOne<Feeder>().WithMany()
            .HasForeignKey(control => new { control.FeederId, control.CommuneId })
            .HasPrincipalKey(feeder => new { feeder.FeederId, feeder.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<IotNode>().WithMany()
            .HasForeignKey(control => new { control.NodeId, control.CommuneId })
            .HasPrincipalKey(node => new { node.NodeId, node.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        // Two feeders cannot claim the same relay of the same device.
        builder.HasIndex(control => new { control.NodeId, control.RelayNo })
            .IsUnique()
            .HasDatabaseName("ux_feeder_control_node_id_relay_no");

        // Explicit: the query filter puts commune_id in every WHERE (partial-index convention).
        builder.HasIndex(control => control.CommuneId);

        builder.HasCommuneReference(control => control.CommuneId);
    }
}
