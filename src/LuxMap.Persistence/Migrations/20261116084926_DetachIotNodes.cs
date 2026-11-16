using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DetachIotNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "detached_at",
                table: "iot_source_registration",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_id",
                table: "iot_node",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.Sql("""
                DROP INDEX ux_iot_source_registration_feeder_id;
                CREATE UNIQUE INDEX ux_iot_source_registration_feeder_id
                    ON iot_source_registration ((profile ->> 'feeder_id'))
                    WHERE detached_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM iot_node WHERE cabinet_id IS NULL)
                        OR EXISTS (SELECT 1 FROM iot_source_registration WHERE detached_at IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back DetachIotNodes while detached nodes or sources exist.';
                    END IF;
                END
                $$;

                DROP INDEX ux_iot_source_registration_feeder_id;
                CREATE UNIQUE INDEX ux_iot_source_registration_feeder_id
                    ON iot_source_registration ((profile ->> 'feeder_id'));
                """);

            migrationBuilder.DropColumn(
                name: "detached_at",
                table: "iot_source_registration");

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_id",
                table: "iot_node",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
