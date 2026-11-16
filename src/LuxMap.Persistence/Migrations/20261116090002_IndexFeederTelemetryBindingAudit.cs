using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LuxMap.Persistence.Migrations;

[DbContext(typeof(LuxMapDbContext))]
[Migration("20261116090002_IndexFeederTelemetryBindingAudit")]
public sealed class IndexFeederTelemetryBindingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE INDEX ix_iot_audit_feeder_sources
                ON iot_registration_audit ((details ->> 'feeder_id'), node_id)
                WHERE details ? 'nominal_voltage';

            CREATE INDEX ix_iot_audit_node_source_timeline
                ON iot_registration_audit (node_id, created_at, id)
                WHERE (details ? 'feeder_id' AND details ? 'nominal_voltage')
                    OR details ->> 'action' IN ('node_detached', 'source_removed');
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX ix_iot_audit_node_source_timeline;
            DROP INDEX ix_iot_audit_feeder_sources;
            """);
}
