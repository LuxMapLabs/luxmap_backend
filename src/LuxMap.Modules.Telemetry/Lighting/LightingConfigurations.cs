using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Telemetry.Lighting;

public sealed class LightingRequestConfiguration : IEntityTypeConfiguration<LightingRequest>
{
    public void Configure(EntityTypeBuilder<LightingRequest> builder)
    {
        builder.ToTable("lighting_request", table =>
        {
            table.HasCheckConstraint("ck_lighting_request_uncontrollable_pole_count", "uncontrollable_pole_count >= 0");
            table.HasCheckConstraint("ck_lighting_request_excluded_array", "jsonb_typeof(excluded) = 'array'");
        });
        builder.HasKey(request => request.RequestId);
        builder.Property(request => request.RequestHash).HasColumnType("text").IsRequired();
        builder.Property(request => request.TargetId).HasColumnType("text").IsRequired();
        builder.Property(request => request.RequestedBy).HasColumnType("text").IsRequired();
        builder.Property(request => request.AffectedSegmentIds).HasColumnType("text[]").IsRequired();
        builder.Property(request => request.Excluded).HasColumnType("jsonb").IsRequired();
        builder.HasContractEnum(request => request.TargetKind);
        builder.HasContractEnum(request => request.RequestedMode);

        builder.HasIndex(request => request.ClientOpId).IsUnique().HasDatabaseName("ux_lighting_request_client_op_id");
        builder.HasIndex(request => request.CommuneId);
        builder.HasIndex(request => request.RequestedBy);

        // Who pressed the button is the point of the row: never cascade, never orphan.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(request => request.RequestedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasCommuneScope();
        builder.HasCommuneReference(request => request.CommuneId);
    }
}

public sealed class LightingCommandConfiguration : IEntityTypeConfiguration<LightingCommand>
{
    public void Configure(EntityTypeBuilder<LightingCommand> builder)
    {
        builder.ToTable("lighting_command", table =>
        {
            table.HasCheckConstraint("ck_lighting_command_relay_no_positive", "relay_no > 0");
            table.HasCheckConstraint("ck_lighting_command_data_source_not_field", "data_source IN ('calibration_rig', 'simulated')");
            table.HasCheckConstraint("ck_lighting_command_error_length", "error IS NULL OR char_length(error) BETWEEN 1 AND 500");
            table.HasCheckConstraint("ck_lighting_command_times_ordered",
                "expires_at > created_at"
                + " AND (delivered_at IS NULL OR delivered_at >= created_at)"
                + " AND (completed_at IS NULL OR completed_at >= COALESCE(delivered_at, created_at))");

            // Each status says exactly which columns it carries (3.3).
            table.HasCheckConstraint("ck_lighting_command_status_columns",
                "CASE status"
                + " WHEN 'pending' THEN delivered_at IS NULL AND completed_at IS NULL AND reported_mode IS NULL AND error IS NULL"
                + " WHEN 'delivered' THEN delivered_at IS NOT NULL AND completed_at IS NULL AND reported_mode IS NULL AND error IS NULL"
                + " WHEN 'applied' THEN delivered_at IS NOT NULL AND completed_at IS NOT NULL AND reported_mode = requested_mode AND error IS NULL"
                + " WHEN 'failed' THEN delivered_at IS NOT NULL AND completed_at IS NOT NULL AND error IS NOT NULL"
                + " ELSE completed_at IS NOT NULL AND reported_mode IS NULL AND error IS NULL"
                + " END");
        });

        builder.HasKey(command => command.CommandId);
        builder.Property(command => command.CommandId).HasPrefixedId(PrefixedIds.LightingCommand);
        builder.Property(command => command.NodeId).HasColumnType("text").IsRequired();
        builder.Property(command => command.FeederId).HasColumnType("text").IsRequired();
        builder.Property(command => command.CabinetId).HasColumnType("text").IsRequired();
        builder.Property(command => command.Error).HasColumnType("text");

        // D-10: the execution order. An identity column is a sequence the database draws at INSERT, strictly increasing
        // across sessions; two presses on one device are serialised by the device lock, so the later one gets the larger seq.
        builder.Property(command => command.Seq).UseIdentityAlwaysColumn();
        builder.HasIndex(command => command.Seq).IsUnique().HasDatabaseName("ux_lighting_command_seq");

        builder.HasContractEnum(command => command.DataSource);
        builder.HasContractEnum(command => command.RequestedMode);
        builder.HasContractEnum(command => command.Status);
        builder.HasContractEnum(command => command.ReportedMode);
        builder.Ignore(command => command.IsOpen);

        builder.HasOne<LightingRequest>().WithMany().HasForeignKey(command => command.RequestId).OnDelete(DeleteBehavior.Restrict);

        // Both identity keys carry commune_id: a command can only name a device and a feeder of its own commune.
        builder.HasOne<IotNode>().WithMany()
            .HasForeignKey(command => new { command.NodeId, command.CommuneId })
            .HasPrincipalKey(node => new { node.NodeId, node.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Feeder>().WithMany()
            .HasForeignKey(command => new { command.FeederId, command.CommuneId })
            .HasPrincipalKey(feeder => new { feeder.FeederId, feeder.CommuneId })
            .OnDelete(DeleteBehavior.Restrict);

        // The device poll: open commands of one device. The full index stays for the composite FK (partial-index rule).
        builder.HasIndex(command => new { command.NodeId, command.Status }).HasDatabaseName("ix_lighting_command_node_id_status");
        builder.HasIndex(command => command.RequestId);
        builder.HasIndex(command => command.CommuneId);

        builder.HasCommuneScope();
        builder.HasCommuneReference(command => command.CommuneId);
    }
}
