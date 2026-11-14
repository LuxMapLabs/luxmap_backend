using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidencePole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pole_id",
                table: "repair_evidence",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_pole_id",
                table: "repair_evidence",
                column: "pole_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_repair_evidence_fault_no_pole",
                table: "repair_evidence",
                sql: "fault_id IS NULL OR pole_id IS NULL");

            // DB-only: reuse the photo target key without making Pole.CommuneId immutable in EF.
            migrationBuilder.Sql("ALTER TABLE repair_evidence ADD CONSTRAINT fk_repair_evidence_pole_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to lose an explicit pole link. Roll back the app while keeping this additive schema instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM repair_evidence WHERE pole_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'AddEvidencePole cannot be rolled back: pole links exist (repair_evidence.pole_id)';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("ALTER TABLE repair_evidence DROP CONSTRAINT fk_repair_evidence_pole_same_commune;");

            migrationBuilder.DropIndex(
                name: "ix_repair_evidence_pole_id",
                table: "repair_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_repair_evidence_fault_no_pole",
                table: "repair_evidence");

            migrationBuilder.DropColumn(
                name: "pole_id",
                table: "repair_evidence");
        }
    }
}
