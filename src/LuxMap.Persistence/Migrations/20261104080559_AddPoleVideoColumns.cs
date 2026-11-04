using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPoleVideoColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.AddColumn<long>(
                name: "video_bytes",
                table: "work_order_pole_photo",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "video_confidence_max",
                table: "work_order_pole_photo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "video_confidence_median",
                table: "work_order_pole_photo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "video_confidence_min",
                table: "work_order_pole_photo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_daylight_frames",
                table: "work_order_pole_photo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_duration_ms",
                table: "work_order_pole_photo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "video_extractor_version_id",
                table: "work_order_pole_photo",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "video_hdr",
                table: "work_order_pole_photo",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "video_median_luma",
                table: "work_order_pole_photo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_object_key",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_off_frames",
                table: "work_order_pole_photo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_on_frames",
                table: "work_order_pole_photo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_reason_code",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_sampled_frames",
                table: "work_order_pole_photo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_sha256",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "video_viewed_at",
                table: "work_order_pole_photo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_viewed_by",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_photo_video_extractor_version",
                table: "work_order_pole_photo",
                column: "video_extractor_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_photo_video_viewed_by",
                table: "work_order_pole_photo",
                column: "video_viewed_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_video_complete",
                table: "work_order_pole_photo",
                sql: "(video_object_key IS NULL AND video_sha256 IS NULL AND video_bytes IS NULL AND video_duration_ms IS NULL AND video_sampled_frames IS NULL AND video_on_frames IS NULL AND video_off_frames IS NULL AND video_daylight_frames IS NULL AND video_median_luma IS NULL AND video_hdr IS NULL AND video_extractor_version_id IS NULL) OR (video_object_key IS NOT NULL AND video_sha256 IS NOT NULL AND video_bytes IS NOT NULL AND video_duration_ms IS NOT NULL AND video_sampled_frames IS NOT NULL AND video_on_frames IS NOT NULL AND video_off_frames IS NOT NULL AND video_daylight_frames IS NOT NULL AND video_median_luma IS NOT NULL AND video_hdr IS NOT NULL AND video_extractor_version_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_video_confidence",
                table: "work_order_pole_photo",
                sql: "(video_confidence_min IS NULL AND video_confidence_median IS NULL AND video_confidence_max IS NULL AND (video_object_key IS NULL OR video_on_frames + video_off_frames = 0)) OR (video_object_key IS NOT NULL AND video_on_frames + video_off_frames > 0 AND video_confidence_min IS NOT NULL AND video_confidence_median IS NOT NULL AND video_confidence_max IS NOT NULL AND video_confidence_min BETWEEN 0 AND 1 AND video_confidence_median BETWEEN 0 AND 1 AND video_confidence_max BETWEEN 0 AND 1 AND video_confidence_min <> 'NaN'::float8 AND video_confidence_median <> 'NaN'::float8 AND video_confidence_max <> 'NaN'::float8 AND video_confidence_min <= video_confidence_median AND video_confidence_median <= video_confidence_max)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_video_reason",
                table: "work_order_pole_photo",
                sql: "video_reason_code IS NULL OR (video_object_key IS NOT NULL AND btrim(video_reason_code) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_video_values",
                table: "work_order_pole_photo",
                sql: "video_object_key IS NULL OR (btrim(video_object_key) <> '' AND video_sha256 ~ '^[0-9a-f]{64}$' AND video_bytes > 0 AND video_duration_ms > 0 AND video_sampled_frames > 0 AND video_on_frames >= 0 AND video_off_frames >= 0 AND video_daylight_frames >= 0 AND video_on_frames + video_off_frames + video_daylight_frames <= video_sampled_frames AND video_median_luma BETWEEN 0 AND 1 AND video_median_luma <> 'NaN'::float8)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_video_viewed",
                table: "work_order_pole_photo",
                sql: "(video_viewed_by IS NULL AND video_viewed_at IS NULL) OR (video_viewed_by IS NOT NULL AND video_viewed_at IS NOT NULL AND video_object_key IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated', 'viewed')");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_photo_video_extractor_version",
                table: "work_order_pole_photo",
                column: "video_extractor_version_id",
                principalTable: "artifact_version",
                principalColumn: "version_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pole_photo_video_viewed_by",
                table: "work_order_pole_photo",
                column: "video_viewed_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM work_order_pole_photo WHERE video_object_key IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM audit_event WHERE action = 'viewed') THEN
                        RAISE EXCEPTION 'Cannot roll back AddPoleVideoColumns while pole video data exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_pole_photo_video_extractor_version",
                table: "work_order_pole_photo");

            migrationBuilder.DropForeignKey(
                name: "fk_pole_photo_video_viewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_pole_photo_video_extractor_version",
                table: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_pole_photo_video_viewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_video_complete",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_video_confidence",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_video_reason",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_video_values",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_video_viewed",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "video_bytes",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_confidence_max",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_confidence_median",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_confidence_min",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_daylight_frames",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_duration_ms",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_extractor_version_id",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_hdr",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_median_luma",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_object_key",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_off_frames",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_on_frames",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_reason_code",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_sampled_frames",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_sha256",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_viewed_at",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "video_viewed_by",
                table: "work_order_pole_photo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
        }
    }
}
