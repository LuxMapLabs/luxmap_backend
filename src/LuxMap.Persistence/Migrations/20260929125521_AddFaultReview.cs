using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaultReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.AddColumn<string>(
                name: "override_fault_type",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "fault",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault",
                sql: "\"override_fault_type\" IS NULL OR \"override_fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_override_is_lamp_class",
                table: "fault",
                sql: "override_fault_type IS NULL OR override_fault_type IN ('lamp_out', 'lamp_dim')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_review_note_not_blank",
                table: "fault",
                sql: "review_note IS NULL OR btrim(review_note) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_override_fault_type",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_override_is_lamp_class",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_review_note_not_blank",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "override_fault_type",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "fault");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order')");
        }
    }
}
