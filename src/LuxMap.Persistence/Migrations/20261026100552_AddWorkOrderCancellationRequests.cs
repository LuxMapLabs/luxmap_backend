using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderCancellationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reject_note",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancellation_rejected_at",
                table: "work_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_request_note",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_request_status",
                table: "work_order",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTime>(
                name: "cancellation_requested_at",
                table: "work_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_cancellation_request_state",
                table: "work_order",
                sql: "(cancellation_request_status = 'none' AND cancellation_request_note IS NULL AND cancellation_requested_at IS NULL\n    AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)\nOR (wo_status = 'assigned' AND cancellation_request_note IS NOT NULL\n    AND char_length(btrim(cancellation_request_note)) BETWEEN 1 AND 500 AND cancellation_requested_at IS NOT NULL\n    AND ((cancellation_request_status = 'pending' AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)\n        OR (cancellation_request_status = 'rejected' AND cancellation_reject_note IS NOT NULL\n            AND char_length(btrim(cancellation_reject_note)) BETWEEN 1 AND 500\n            AND cancellation_rejected_at IS NOT NULL AND cancellation_rejected_at >= cancellation_requested_at)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_cancellation_request_status",
                table: "work_order",
                sql: "\"cancellation_request_status\" IN ('none', 'pending', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_cancellation_requested', 'work_order_cancellation_rejected', 'work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM work_order WHERE cancellation_request_status <> 'none'
                        OR cancellation_request_note IS NOT NULL OR cancellation_requested_at IS NOT NULL
                        OR cancellation_reject_note IS NOT NULL OR cancellation_rejected_at IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM notification WHERE type IN ('work_order_cancellation_requested', 'work_order_cancellation_rejected'))
                        OR EXISTS (SELECT 1 FROM audit_event WHERE action IN ('cancellation_requested', 'cancellation_rejected')) THEN
                        RAISE EXCEPTION 'Cannot roll back AddWorkOrderCancellationRequests while cancellation data exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_cancellation_request_state",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_cancellation_request_status",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "cancellation_reject_note",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_rejected_at",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_request_note",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_request_status",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_requested_at",
                table: "work_order");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
        }
    }
}
