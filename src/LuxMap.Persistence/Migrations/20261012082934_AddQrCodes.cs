using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQrCodes : Migration
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

            migrationBuilder.CreateSequence(
                name: "qr_id_seq");

            migrationBuilder.CreateTable(
                name: "qr_code",
                columns: table => new
                {
                    qr_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('QR', nextval('qr_id_seq'), 4)"),
                    code = table.Column<string>(type: "text", nullable: false),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_by = table.Column<string>(type: "text", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qr_code", x => x.qr_id);
                    table.CheckConstraint("ck_qr_code_code_format", "code ~ '^[0-9A-Za-z]{16}$'");
                    table.CheckConstraint("ck_qr_code_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.CheckConstraint("ck_qr_code_revoked_together", "(revoked_at IS NULL) = (revoked_by IS NULL)");
                    table.ForeignKey(
                        name: "fk_qr_code_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qr_code_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qr_code_app_user_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_qr_code_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code')");

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_commune_id",
                table: "qr_code",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_created_by",
                table: "qr_code",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_pole_id",
                table: "qr_code",
                column: "pole_id");

            migrationBuilder.CreateIndex(
                name: "ix_qr_code_revoked_by",
                table: "qr_code",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "ux_qr_code_code",
                table: "qr_code",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_qr_code_pole_id_active",
                table: "qr_code",
                column: "pole_id",
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "qr_code");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "qr_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command')");
        }
    }
}
