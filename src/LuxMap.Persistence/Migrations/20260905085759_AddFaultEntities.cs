using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaultEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "cluster_id_seq");

            migrationBuilder.CreateSequence(
                name: "fault_id_seq");

            migrationBuilder.CreateTable(
                name: "fault_cluster",
                columns: table => new
                {
                    cluster_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CLS', nextval('cluster_id_seq'), 3)"),
                    segment_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    clustered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    clustering_model_version = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fault_cluster", x => x.cluster_id);
                    table.ForeignKey(
                        name: "fk_fault_cluster_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_cluster_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fault",
                columns: table => new
                {
                    fault_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('FAULT', nextval('fault_id_seq'), 4)"),
                    client_op_id = table.Column<string>(type: "text", nullable: true),
                    pole_id = table.Column<string>(type: "text", nullable: true),
                    fixture_id = table.Column<string>(type: "text", nullable: true),
                    segment_id = table.Column<string>(type: "text", nullable: true),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: true),
                    lng = table.Column<double>(type: "double precision", nullable: true),
                    fault_type = table.Column<string>(type: "text", nullable: false),
                    fault_status = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    source_channel = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    priority_score = table.Column<double>(type: "double precision", nullable: true),
                    status_confidence = table.Column<double>(type: "double precision", nullable: true),
                    cluster_id = table.Column<string>(type: "text", nullable: true),
                    detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    note = table.Column<string>(type: "text", nullable: true),
                    reported_by = table.Column<string>(type: "text", nullable: true),
                    confirmed_by = table.Column<string>(type: "text", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    detection_model_version = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fault", x => x.fault_id);
                    table.CheckConstraint("ck_fault_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_fault_fault_status", "\"fault_status\" IN ('detected', 'confirmed', 'rejected', 'in_progress', 'resolved', 'verified')");
                    table.CheckConstraint("ck_fault_fault_type", "\"fault_type\" IN ('lamp_out', 'lamp_dim', 'segment_outage', 'node_offline', 'runtime_decline')");
                    table.CheckConstraint("ck_fault_location_finite", "(lat IS NULL OR (lat <> 'NaN'::float8 AND lat <> 'Infinity'::float8 AND lat <> '-Infinity'::float8 AND lat BETWEEN -90 AND 90)) AND (lng IS NULL OR (lng <> 'NaN'::float8 AND lng <> 'Infinity'::float8 AND lng <> '-Infinity'::float8 AND lng BETWEEN -180 AND 180))");
                    table.CheckConstraint("ck_fault_pole_or_location", "pole_id IS NOT NULL OR (lat IS NOT NULL AND lng IS NOT NULL)");
                    table.CheckConstraint("ck_fault_priority_score_finite", "priority_score IS NULL OR (priority_score <> 'NaN'::float8 AND priority_score <> 'Infinity'::float8 AND priority_score <> '-Infinity'::float8)");
                    table.CheckConstraint("ck_fault_severity", "\"severity\" IN ('low', 'medium', 'high', 'critical')");
                    table.CheckConstraint("ck_fault_source_channel", "\"source_channel\" IN ('cv', 'iot', 'field_report')");
                    table.CheckConstraint("ck_fault_status_confidence_range", "status_confidence IS NULL OR (status_confidence >= 0 AND status_confidence <= 1 AND status_confidence <> 'NaN'::float8 AND status_confidence <> 'Infinity'::float8 AND status_confidence <> '-Infinity'::float8)");
                    table.ForeignKey(
                        name: "fk_fault_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_app_user_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_app_user_reported_by",
                        column: x => x.reported_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_app_user_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_fault_cluster_cluster_id",
                        column: x => x.cluster_id,
                        principalTable: "fault_cluster",
                        principalColumn: "cluster_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_fixture_fixture_id",
                        column: x => x.fixture_id,
                        principalTable: "fixture",
                        principalColumn: "fixture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fault_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fault_cluster_id",
                table: "fault",
                column: "cluster_id");

            migrationBuilder.CreateIndex(
                name: "ix_fault_commune_id",
                table: "fault",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_fault_confirmed_by",
                table: "fault",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ix_fault_fault_status",
                table: "fault",
                column: "fault_status");

            migrationBuilder.CreateIndex(
                name: "ix_fault_fixture_id",
                table: "fault",
                column: "fixture_id");

            migrationBuilder.CreateIndex(
                name: "ix_fault_pole_id",
                table: "fault",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ix_fault_priority_score",
                table: "fault",
                column: "priority_score",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_fault_reported_by",
                table: "fault",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "ix_fault_resolved_by",
                table: "fault",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_fault_segment_id",
                table: "fault",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ux_fault_client_op_id",
                table: "fault",
                column: "client_op_id",
                unique: true,
                filter: "client_op_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fault_cluster_commune_id",
                table: "fault_cluster",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_fault_cluster_segment_id",
                table: "fault_cluster",
                column: "segment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fault");

            migrationBuilder.DropTable(
                name: "fault_cluster");

            migrationBuilder.DropSequence(
                name: "cluster_id_seq");

            migrationBuilder.DropSequence(
                name: "fault_id_seq");
        }
    }
}
