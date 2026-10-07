using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <summary>
    /// Drops CAB-5 — "no device on a <c>field</c> cabinet" — together with the three constraints and the
    /// <c>iot_node.cabinet_data_source</c> copy that existed only to enforce it (Mỹ, 07/10/2026: the rule is not
    /// needed). The device's own rule stays: <c>ck_iot_node_data_source_not_field</c>.
    /// </summary>
    /// <remarks>
    /// The provenance key and its unique target are raw SQL from <c>AddElectricalCabinet</c>, unknown to the EF
    /// model, so they are dropped by name here — the target sits on <c>electrical_cabinet</c> and would outlive
    /// the column. ⚠️ <c>Down()</c> fails while any device sits on a <c>field</c> cabinet: the restored CHECK
    /// refuses that row, which is the rule coming back.
    /// </remarks>
    public partial class DropFieldCabinetRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE iot_node DROP CONSTRAINT fk_iot_node_cabinet_data_source;
                ALTER TABLE electrical_cabinet DROP CONSTRAINT ux_electrical_cabinet_cabinet_id_data_source;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_iot_node_cabinet_data_source",
                table: "iot_node");

            migrationBuilder.DropCheckConstraint(
                name: "ck_iot_node_cabinet_not_field",
                table: "iot_node");

            migrationBuilder.DropColumn(
                name: "cabinet_data_source",
                table: "iot_node");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cabinet_data_source",
                table: "iot_node",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE iot_node AS node
                SET cabinet_data_source = cabinet.data_source
                FROM electrical_cabinet AS cabinet
                WHERE cabinet.cabinet_id = node.cabinet_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "cabinet_data_source",
                table: "iot_node",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_iot_node_cabinet_data_source",
                table: "iot_node",
                sql: "\"cabinet_data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_iot_node_cabinet_not_field",
                table: "iot_node",
                sql: "cabinet_data_source <> 'field'");

            migrationBuilder.Sql("""
                ALTER TABLE electrical_cabinet ADD CONSTRAINT ux_electrical_cabinet_cabinet_id_data_source UNIQUE (cabinet_id, data_source);
                ALTER TABLE iot_node ADD CONSTRAINT fk_iot_node_cabinet_data_source
                    FOREIGN KEY (cabinet_id, cabinet_data_source) REFERENCES electrical_cabinet (cabinet_id, data_source)
                    ON UPDATE CASCADE ON DELETE RESTRICT;
                """);
        }
    }
}
