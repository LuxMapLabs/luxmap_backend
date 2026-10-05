using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "notification_id_seq");

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    notification_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('NTF', nextval('notification_id_seq'), 6)"),
                    recipient_user_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.notification_id);
                    table.CheckConstraint("ck_notification_body_length", "char_length(body) BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_notification_entity_type", "\"entity_type\" IN ('work_order', 'survey_sweep', 'fault')");
                    table.CheckConstraint("ck_notification_title_length", "char_length(title) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_notification_type", "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported')");
                    table.ForeignKey(
                        name: "fk_notification_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_app_user_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_commune_id",
                table: "notification",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipient_created",
                table: "notification",
                columns: new[] { "recipient_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipient_unread",
                table: "notification",
                column: "recipient_user_id",
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropSequence(
                name: "notification_id_seq");
        }
    }
}
