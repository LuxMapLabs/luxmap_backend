using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LuxMap.Persistence.Migrations;

[DbContext(typeof(LuxMapDbContext))]
[Migration("20261114120000_UniqueIotSourceFeeder")]
public sealed class UniqueIotSourceFeeder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing registrations already store their feeder in the profile.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX ux_iot_source_registration_feeder_id
                ON iot_source_registration ((profile ->> 'feeder_id'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX ux_iot_source_registration_feeder_id;");
    }
}
