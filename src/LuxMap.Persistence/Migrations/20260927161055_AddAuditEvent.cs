using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_event",
                columns: table => new
                {
                    audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_kind = table.Column<string>(type: "text", nullable: false),
                    actor_user_id = table.Column<string>(type: "text", nullable: true),
                    actor_role = table.Column<string>(type: "text", nullable: true),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.audit_id);
                    table.CheckConstraint("ck_audit_event_action", "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed')");
                    table.CheckConstraint("ck_audit_event_actor", "(actor_kind = 'user') = (actor_user_id IS NOT NULL) AND (actor_user_id IS NULL) = (actor_role IS NULL)");
                    table.CheckConstraint("ck_audit_event_actor_kind", "\"actor_kind\" IN ('user', 'cv', 'iot')");
                    table.CheckConstraint("ck_audit_event_actor_role", "\"actor_role\" IS NULL OR \"actor_role\" IN ('superior', 'manager', 'field_engineer', 'system_admin')");
                    table.CheckConstraint("ck_audit_event_after_state_object", "after_state IS NULL OR jsonb_typeof(after_state) = 'object'");
                    table.CheckConstraint("ck_audit_event_before_state_object", "before_state IS NULL OR jsonb_typeof(before_state) = 'object'");
                    table.CheckConstraint("ck_audit_event_entity_type", "\"entity_type\" IN ('work_order')");
                    table.CheckConstraint("ck_audit_event_has_state", "before_state IS NOT NULL OR after_state IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_audit_event_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_event_app_user_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_actor_user_id",
                table: "audit_event",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_commune_id",
                table: "audit_event",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_entity",
                table: "audit_event",
                columns: new[] { "entity_type", "entity_id", "audit_id" });

            // The GUC is a test teardown escape hatch, not a SQL privilege boundary.
            migrationBuilder.Sql("""
                CREATE FUNCTION luxmap_audit_event_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF current_setting('luxmap.audit_purge', true) IS DISTINCT FROM 'on' THEN
                        RAISE EXCEPTION 'audit_event is append-only' USING ERRCODE = '55000';
                    END IF;
                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    IF TG_OP = 'UPDATE' THEN
                        RETURN NEW;
                    END IF;
                    RETURN NULL;
                END;
                $$;
                CREATE TRIGGER audit_event_append_only_rows
                    BEFORE UPDATE OR DELETE ON audit_event
                    FOR EACH ROW EXECUTE FUNCTION luxmap_audit_event_append_only();
                CREATE TRIGGER audit_event_append_only_truncate
                    BEFORE TRUNCATE ON audit_event
                    FOR EACH STATEMENT EXECUTE FUNCTION luxmap_audit_event_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER audit_event_append_only_rows ON audit_event;
                DROP TRIGGER audit_event_append_only_truncate ON audit_event;
                DROP FUNCTION luxmap_audit_event_append_only();
                """);
            migrationBuilder.DropTable(
                name: "audit_event");
        }
    }
}
