using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolePhotoReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_decision",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_current_status_confidence_matches_status",
                table: "pole_current_status");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault");

            migrationBuilder.AddColumn<string>(
                name: "final_cv_state",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "work_order_pole_photo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_by",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_photo_reviewed_by",
                table: "work_order_pole_photo",
                column: "reviewed_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_decision",
                table: "work_order_pole_photo",
                sql: "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (COALESCE(final_cv_state, cv_state) IS NOT NULL AND COALESCE(final_cv_state, cv_state) IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_review_complete",
                table: "work_order_pole_photo",
                sql: "(final_cv_state IS NULL AND review_note IS NULL AND reviewed_by IS NULL AND reviewed_at IS NULL) OR (final_cv_state IS NOT NULL AND final_cv_state IN ('on','off') AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_review_note",
                table: "work_order_pole_photo",
                sql: "review_note IS NULL OR (char_length(review_note) BETWEEN 1 AND 2000 AND btrim(review_note, E' \\t\\n\\r\\f' || chr(11)) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_review_note_required",
                table: "work_order_pole_photo",
                sql: "final_cv_state IS NULL OR final_cv_state IS NOT DISTINCT FROM cv_state OR review_note IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_current_status_confidence_matches_status",
                table: "pole_current_status",
                sql: "fixture_status <> 'unknown' OR status_confidence IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault",
                sql: "origin_pole_photo_id IS NULL OR (source_channel IN ('cv','field_report') AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL AND (source_channel <> 'field_report' OR (reported_by IS NOT NULL AND status_confidence IS NULL AND detection_model_version IS NULL)))");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_photo_reviewed_by",
                table: "work_order_pole_photo",
                column: "reviewed_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never discard reviews or invent scores/sources to make the old schema accept new data.
            // Hold these locks until the migration transaction ends, so preflight cannot race a writer.
            migrationBuilder.Sql("""
                LOCK TABLE work_order_pole_photo, pole_current_status, fault IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM work_order_pole_photo WHERE final_cv_state IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM pole_current_status WHERE fixture_status <> 'unknown' AND status_confidence IS NULL)
                        OR EXISTS (SELECT 1 FROM fault WHERE origin_pole_photo_id IS NOT NULL AND source_channel <> 'cv') THEN
                        RAISE EXCEPTION 'Cannot roll back AddPolePhotoReviews with review data; roll back the application and keep the schema.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_pole_photo_reviewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_pole_photo_reviewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_decision",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_review_complete",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_review_note",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_review_note_required",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_current_status_confidence_matches_status",
                table: "pole_current_status");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "final_cv_state",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_decision",
                table: "work_order_pole_photo",
                sql: "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (cv_state IS NOT NULL AND cv_state IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_current_status_confidence_matches_status",
                table: "pole_current_status",
                sql: "(status_confidence IS NULL) = (fixture_status = 'unknown')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fault_pole_photo_source",
                table: "fault",
                sql: "origin_pole_photo_id IS NULL OR (source_channel = 'cv' AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL)");
        }
    }
}
