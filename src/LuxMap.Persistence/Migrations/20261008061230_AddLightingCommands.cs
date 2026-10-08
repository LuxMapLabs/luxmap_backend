using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLightingCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "command_id_seq");

            migrationBuilder.AddColumn<long>(
                name: "mode_seq",
                table: "feeder_control",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lighting_request",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    target_kind = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<string>(type: "text", nullable: false),
                    requested_mode = table.Column<string>(type: "text", nullable: false),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    affected_segment_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    excluded = table.Column<string>(type: "jsonb", nullable: false),
                    uncontrollable_pole_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lighting_request", x => x.request_id);
                    table.CheckConstraint("ck_lighting_request_excluded_array", "jsonb_typeof(excluded) = 'array'");
                    table.CheckConstraint("ck_lighting_request_requested_mode", "\"requested_mode\" IN ('on', 'off', 'auto')");
                    table.CheckConstraint("ck_lighting_request_target_kind", "\"target_kind\" IN ('feeder', 'segment')");
                    table.CheckConstraint("ck_lighting_request_uncontrollable_pole_count", "uncontrollable_pole_count >= 0");
                    table.ForeignKey(
                        name: "fk_lighting_request_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lighting_request_app_user_requested_by",
                        column: x => x.requested_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lighting_command",
                columns: table => new
                {
                    command_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CMD', nextval('command_id_seq'), 6)"),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "text", nullable: false),
                    feeder_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    relay_no = table.Column<short>(type: "smallint", nullable: false),
                    cabinet_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    requested_mode = table.Column<string>(type: "text", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reported_mode = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lighting_command", x => x.command_id);
                    table.CheckConstraint("ck_lighting_command_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_lighting_command_data_source_not_field", "data_source IN ('calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_lighting_command_error_length", "error IS NULL OR char_length(error) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_lighting_command_relay_no_positive", "relay_no > 0");
                    table.CheckConstraint("ck_lighting_command_reported_mode", "\"reported_mode\" IS NULL OR \"reported_mode\" IN ('on', 'off', 'auto')");
                    table.CheckConstraint("ck_lighting_command_requested_mode", "\"requested_mode\" IN ('on', 'off', 'auto')");
                    table.CheckConstraint("ck_lighting_command_status", "\"status\" IN ('pending', 'delivered', 'applied', 'failed', 'expired', 'superseded')");
                    table.CheckConstraint("ck_lighting_command_status_columns", "CASE status WHEN 'pending' THEN delivered_at IS NULL AND completed_at IS NULL AND reported_mode IS NULL AND error IS NULL WHEN 'delivered' THEN delivered_at IS NOT NULL AND completed_at IS NULL AND reported_mode IS NULL AND error IS NULL WHEN 'applied' THEN delivered_at IS NOT NULL AND completed_at IS NOT NULL AND reported_mode IS NOT NULL AND reported_mode = requested_mode AND error IS NULL WHEN 'failed' THEN delivered_at IS NOT NULL AND completed_at IS NOT NULL AND error IS NOT NULL ELSE completed_at IS NOT NULL AND reported_mode IS NULL AND error IS NULL END");
                    table.CheckConstraint("ck_lighting_command_times_ordered", "expires_at > created_at AND (delivered_at IS NULL OR delivered_at >= created_at) AND (completed_at IS NULL OR completed_at >= COALESCE(delivered_at, created_at))");
                    table.ForeignKey(
                        name: "fk_lighting_command_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lighting_command_feeder_feeder_id_commune_id",
                        columns: x => new { x.feeder_id, x.commune_id },
                        principalTable: "feeder",
                        principalColumns: new[] { "feeder_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lighting_command_iot_node_node_id_commune_id",
                        columns: x => new { x.node_id, x.commune_id },
                        principalTable: "iot_node",
                        principalColumns: new[] { "node_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lighting_command_lighting_request_request_id",
                        column: x => x.request_id,
                        principalTable: "lighting_request",
                        principalColumn: "request_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_feeder_control_mode_seq_has_mode",
                table: "feeder_control",
                sql: "mode_seq IS NULL OR control_mode IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event",
                sql: "\"actor_kind\" IN ('user', 'cv', 'iot', 'system')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command')");

            migrationBuilder.CreateIndex(
                name: "ix_lighting_command_commune_id",
                table: "lighting_command",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_lighting_command_feeder_id_commune_id",
                table: "lighting_command",
                columns: new[] { "feeder_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lighting_command_node_id_commune_id",
                table: "lighting_command",
                columns: new[] { "node_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lighting_command_node_id_status",
                table: "lighting_command",
                columns: new[] { "node_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lighting_command_request_id",
                table: "lighting_command",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ux_lighting_command_seq",
                table: "lighting_command",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lighting_request_commune_id",
                table: "lighting_request",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_lighting_request_requested_by",
                table: "lighting_request",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ux_lighting_request_client_op_id",
                table: "lighting_request",
                column: "client_op_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lighting_command");

            migrationBuilder.DropTable(
                name: "lighting_request");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feeder_control_mode_seq_has_mode",
                table: "feeder_control");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "mode_seq",
                table: "feeder_control");

            migrationBuilder.DropSequence(
                name: "command_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_actor_kind",
                table: "audit_event",
                sql: "\"actor_kind\" IN ('user', 'cv', 'iot')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep')");
        }
    }
}
