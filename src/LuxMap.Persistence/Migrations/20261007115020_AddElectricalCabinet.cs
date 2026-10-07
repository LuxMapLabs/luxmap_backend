using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <summary>
    /// CABINET (drift CAB-1…CAB-5): the main electrical cabinet becomes an asset of its own and an IoT device is
    /// equipment mounted in one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hand-ordered, not as scaffolded: EF dropped <c>iot_node.geom</c> FIRST and added the new NOT NULL columns
    /// with a <c>''</c> default, which would lose every device's position and fail the foreign keys on any
    /// database that holds a device. Here each existing device gets a cabinet at its own point BEFORE the column
    /// goes.
    /// </para>
    /// <para>
    /// 🔴 <b>Six constraints are raw SQL and unknown to the EF model</b> (see <c>CabinetConstraints</c>): their
    /// unique targets include a column people change, which as EF keys would be frozen on tracked entities.
    /// No later migration will recreate them; <c>CabinetConstraintTests</c> pins that they exist.
    /// </para>
    /// <para>
    /// <c>Down()</c> restores the device's point from its cabinet's CURRENT position, not the one it had before
    /// <c>Up()</c>, and cabinets without a device are lost.
    /// </para>
    /// </remarks>
    public partial class AddElectricalCabinet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The table.
            migrationBuilder.CreateSequence(
                name: "cabinet_id_seq");

            migrationBuilder.CreateTable(
                name: "electrical_cabinet",
                columns: table => new
                {
                    cabinet_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('CAB', nextval('cabinet_id_seq'), 3)"),
                    cabinet_name = table.Column<string>(type: "text", nullable: false),
                    external_ref = table.Column<string>(type: "text", nullable: true),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_electrical_cabinet", x => x.cabinet_id);
                    table.UniqueConstraint("ak_electrical_cabinet_cabinet_id_commune_id", x => new { x.cabinet_id, x.commune_id });
                    table.CheckConstraint("ck_electrical_cabinet_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.ForeignKey(
                        name: "fk_electrical_cabinet_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_electrical_cabinet_app_user_updated_by",
                        column: x => x.updated_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_electrical_cabinet_commune_id",
                table: "electrical_cabinet",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_electrical_cabinet_geom",
                table: "electrical_cabinet",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_electrical_cabinet_updated_by",
                table: "electrical_cabinet",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ux_electrical_cabinet_commune_external_ref",
                table: "electrical_cabinet",
                columns: new[] { "commune_id", "external_ref" },
                unique: true,
                filter: "external_ref IS NOT NULL");

            // 2. The new columns, NULLABLE until the backfill has filled them.
            migrationBuilder.AddColumn<string>(
                name: "cabinet_id",
                table: "feeder",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cabinet_id",
                table: "iot_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cabinet_data_source",
                table: "iot_node",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cabinet_id",
                table: "feeder_control",
                type: "text",
                nullable: true);

            // 3. Backfill: one cabinet per existing device, at the device's point, with its commune and provenance.
            // The name carries the node id so the UPDATE can pair them; the table is new, so nothing else matches.
            migrationBuilder.Sql("""
                INSERT INTO electrical_cabinet (cabinet_name, commune_id, geom, data_source)
                SELECT 'Tủ ' || node_id, commune_id, geom, data_source
                FROM iot_node
                ORDER BY created_at, length(node_id), node_id;

                UPDATE iot_node AS node
                SET cabinet_id = cabinet.cabinet_id, cabinet_data_source = cabinet.data_source
                FROM electrical_cabinet AS cabinet
                WHERE cabinet.cabinet_name = 'Tủ ' || node.node_id;

                UPDATE feeder
                SET cabinet_id = node.cabinet_id
                FROM feeder_control AS control
                JOIN iot_node AS node ON node.node_id = control.node_id
                WHERE control.feeder_id = feeder.feeder_id;

                UPDATE feeder_control AS control
                SET cabinet_id = node.cabinet_id
                FROM iot_node AS node
                WHERE node.node_id = control.node_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_id",
                table: "iot_node",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_data_source",
                table: "iot_node",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_id",
                table: "feeder_control",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // 4. The keys the EF model knows.
            migrationBuilder.CreateIndex(
                name: "ix_feeder_cabinet_id_commune_id",
                table: "feeder",
                columns: new[] { "cabinet_id", "commune_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_feeder_electrical_cabinet_cabinet_id_commune_id",
                table: "feeder",
                columns: new[] { "cabinet_id", "commune_id" },
                principalTable: "electrical_cabinet",
                principalColumns: new[] { "cabinet_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_cabinet_id_commune_id",
                table: "iot_node",
                columns: new[] { "cabinet_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_iot_node_cabinet_id",
                table: "iot_node",
                column: "cabinet_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_iot_node_electrical_cabinet_cabinet_id_commune_id",
                table: "iot_node",
                columns: new[] { "cabinet_id", "commune_id" },
                principalTable: "electrical_cabinet",
                principalColumns: new[] { "cabinet_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddCheckConstraint(
                name: "ck_iot_node_cabinet_data_source",
                table: "iot_node",
                sql: "\"cabinet_data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_iot_node_cabinet_not_field",
                table: "iot_node",
                sql: "cabinet_data_source <> 'field'");

            // 5. The keys the EF model does NOT know (CabinetConstraints). CAB-4: a relay switches only a feeder of
            // the device's own cabinet, and a switched feeder or device cannot move. CAB-5: a device never sits on a
            // field cabinet — ON UPDATE CASCADE carries the cabinet's provenance onto the device, where the CHECK
            // above refuses `field`. The cascade rewrites one non-commune column on a same-commune row, so the
            // CommuneWriteGuard blind spot for database cascades (CLAUDE.md 1c) does not open.
            migrationBuilder.Sql("""
                ALTER TABLE feeder ADD CONSTRAINT ux_feeder_feeder_id_cabinet_id UNIQUE (feeder_id, cabinet_id);
                ALTER TABLE iot_node ADD CONSTRAINT ux_iot_node_node_id_cabinet_id UNIQUE (node_id, cabinet_id);
                ALTER TABLE electrical_cabinet ADD CONSTRAINT ux_electrical_cabinet_cabinet_id_data_source UNIQUE (cabinet_id, data_source);

                ALTER TABLE feeder_control ADD CONSTRAINT fk_feeder_control_feeder_same_cabinet
                    FOREIGN KEY (feeder_id, cabinet_id) REFERENCES feeder (feeder_id, cabinet_id) ON DELETE RESTRICT;
                ALTER TABLE feeder_control ADD CONSTRAINT fk_feeder_control_node_same_cabinet
                    FOREIGN KEY (node_id, cabinet_id) REFERENCES iot_node (node_id, cabinet_id) ON DELETE RESTRICT;
                ALTER TABLE iot_node ADD CONSTRAINT fk_iot_node_cabinet_data_source
                    FOREIGN KEY (cabinet_id, cabinet_data_source) REFERENCES electrical_cabinet (cabinet_id, data_source)
                    ON UPDATE CASCADE ON DELETE RESTRICT;
                """);

            // 6. The device's own point goes: the cabinet carries it now (CAB-3).
            migrationBuilder.DropIndex(
                name: "ix_iot_node_geom",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "geom",
                table: "iot_node");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The device's point back, from its cabinet's CURRENT position, before anything that holds it is dropped.
            migrationBuilder.AddColumn<Point>(
                name: "geom",
                table: "iot_node",
                type: "geometry(Point,4326)",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE iot_node AS node
                SET geom = cabinet.geom
                FROM electrical_cabinet AS cabinet
                WHERE cabinet.cabinet_id = node.cabinet_id;
                """);

            migrationBuilder.AlterColumn<Point>(
                name: "geom",
                table: "iot_node",
                type: "geometry(Point,4326)",
                nullable: false,
                oldClrType: typeof(Point),
                oldType: "geometry(Point,4326)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iot_node_geom",
                table: "iot_node",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.Sql("""
                ALTER TABLE iot_node DROP CONSTRAINT fk_iot_node_cabinet_data_source;
                ALTER TABLE feeder_control DROP CONSTRAINT fk_feeder_control_node_same_cabinet;
                ALTER TABLE feeder_control DROP CONSTRAINT fk_feeder_control_feeder_same_cabinet;
                ALTER TABLE electrical_cabinet DROP CONSTRAINT ux_electrical_cabinet_cabinet_id_data_source;
                ALTER TABLE iot_node DROP CONSTRAINT ux_iot_node_node_id_cabinet_id;
                ALTER TABLE feeder DROP CONSTRAINT ux_feeder_feeder_id_cabinet_id;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_iot_node_cabinet_not_field",
                table: "iot_node");

            migrationBuilder.DropCheckConstraint(
                name: "ck_iot_node_cabinet_data_source",
                table: "iot_node");

            migrationBuilder.DropForeignKey(
                name: "fk_iot_node_electrical_cabinet_cabinet_id_commune_id",
                table: "iot_node");

            migrationBuilder.DropIndex(
                name: "ux_iot_node_cabinet_id",
                table: "iot_node");

            migrationBuilder.DropIndex(
                name: "ix_iot_node_cabinet_id_commune_id",
                table: "iot_node");

            migrationBuilder.DropForeignKey(
                name: "fk_feeder_electrical_cabinet_cabinet_id_commune_id",
                table: "feeder");

            migrationBuilder.DropIndex(
                name: "ix_feeder_cabinet_id_commune_id",
                table: "feeder");

            migrationBuilder.DropColumn(
                name: "cabinet_id",
                table: "feeder_control");

            migrationBuilder.DropColumn(
                name: "cabinet_data_source",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "cabinet_id",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "cabinet_id",
                table: "feeder");

            migrationBuilder.DropTable(
                name: "electrical_cabinet");

            migrationBuilder.DropSequence(
                name: "cabinet_id_seq");
        }
    }
}
