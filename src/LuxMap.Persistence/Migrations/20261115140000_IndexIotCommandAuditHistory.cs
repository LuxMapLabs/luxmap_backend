using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LuxMap.Persistence.Migrations;

[DbContext(typeof(LuxMapDbContext))]
[Migration("20261115140000_IndexIotCommandAuditHistory")]
public sealed class IndexIotCommandAuditHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX ix_iot_command_audit_requested
                ON iot_registration_audit (node_id, created_at DESC, id DESC)
                WHERE details ->> 'event_type' = 'command_requested';

            CREATE INDEX ix_iot_command_audit_accepted
                ON iot_registration_audit (node_id, server_id, (details ->> 'command_id'))
                WHERE details ->> 'event_type' = 'command_accepted';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX ix_iot_command_audit_accepted;
            DROP INDEX ix_iot_command_audit_requested;
            """);
    }
}
