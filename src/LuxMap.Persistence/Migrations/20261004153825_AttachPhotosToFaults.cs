using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachPhotosToFaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "work_order_id",
                table: "repair_evidence",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "fault_id",
                table: "repair_evidence",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_fault_id_commune_id",
                table: "repair_evidence",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_repair_evidence_fault_observation",
                table: "repair_evidence",
                sql: "fault_id IS NULL OR kind = 'observation'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_repair_evidence_one_parent",
                table: "repair_evidence",
                sql: "(work_order_id IS NULL) <> (fault_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_repair_evidence_fault_fault_id_commune_id",
                table: "repair_evidence",
                columns: new[] { "fault_id", "commune_id" },
                principalTable: "fault",
                principalColumns: new[] { "fault_id", "commune_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A fault photo has no work order, so work_order_id cannot become NOT NULL again while one exists.
            // Refuse with a message instead of failing inside ALTER COLUMN; never invent a work order for it.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM repair_evidence WHERE fault_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'AttachPhotosToFaults cannot be rolled back: fault photos exist (repair_evidence.fault_id)';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_repair_evidence_fault_fault_id_commune_id",
                table: "repair_evidence");

            migrationBuilder.DropIndex(
                name: "ix_repair_evidence_fault_id_commune_id",
                table: "repair_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_repair_evidence_fault_observation",
                table: "repair_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_repair_evidence_one_parent",
                table: "repair_evidence");

            migrationBuilder.DropColumn(
                name: "fault_id",
                table: "repair_evidence");

            migrationBuilder.AlterColumn<string>(
                name: "work_order_id",
                table: "repair_evidence",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
