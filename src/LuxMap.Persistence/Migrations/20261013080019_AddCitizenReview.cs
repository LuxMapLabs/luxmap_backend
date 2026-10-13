using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.AddColumn<string>(
                name: "ai_artifact_version",
                table: "citizen_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_checked_at",
                table: "citizen_photo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ai_confidence",
                table: "citizen_photo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_hint",
                table: "citizen_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_citizen_photo_ai_confidence",
                table: "citizen_photo",
                sql: "(ai_confidence IS NULL) = (ai_hint IS NULL OR ai_hint = 'inconclusive') AND (ai_confidence IS NULL OR (ai_confidence >= 0 AND ai_confidence <= 1 AND ai_confidence <> 'NaN'::float8))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_citizen_photo_ai_hint",
                table: "citizen_photo",
                sql: "\"ai_hint\" IS NULL OR \"ai_hint\" IN ('lamp_on', 'lamp_off', 'inconclusive')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_citizen_photo_ai_together",
                table: "citizen_photo",
                sql: "(ai_hint IS NULL) = (ai_artifact_version IS NULL) AND (ai_hint IS NULL) = (ai_checked_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_citizen_photo_ai_version_not_blank",
                table: "citizen_photo",
                sql: "ai_artifact_version IS NULL OR btrim(ai_artifact_version) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_citizen_photo_ai_confidence",
                table: "citizen_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_citizen_photo_ai_hint",
                table: "citizen_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_citizen_photo_ai_together",
                table: "citizen_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_citizen_photo_ai_version_not_blank",
                table: "citizen_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "ai_artifact_version",
                table: "citizen_photo");

            migrationBuilder.DropColumn(
                name: "ai_checked_at",
                table: "citizen_photo");

            migrationBuilder.DropColumn(
                name: "ai_confidence",
                table: "citizen_photo");

            migrationBuilder.DropColumn(
                name: "ai_hint",
                table: "citizen_photo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked')");
        }
    }
}
