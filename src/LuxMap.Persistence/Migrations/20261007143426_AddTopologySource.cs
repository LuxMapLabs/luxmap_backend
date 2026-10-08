using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <summary>
    /// TOPO-INFER TI-1 / D-10: provenance labels on the two electrical relations — <c>pole.feeder_source</c> and
    /// <c>feeder.cabinet_source</c> (<c>verified | inferred</c>), each NULL exactly when its relation is.
    /// </summary>
    /// <remarks>Existing relations are backfilled <c>inferred</c>. <c>Down()</c> drops the labels: what was verified is lost.</remarks>
    public partial class AddTopologySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feeder_source",
                table: "pole",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cabinet_source",
                table: "feeder",
                type: "text",
                nullable: true);

            // TI-1 / D-9: every relation that already exists is a claim nobody verified — it becomes `inferred`, never
            // `verified`, and the pairing CHECK below can only be added once no relation is left without a label.
            migrationBuilder.Sql("""
                UPDATE pole SET feeder_source = 'inferred' WHERE feeder_id IS NOT NULL;
                UPDATE feeder SET cabinet_source = 'inferred' WHERE cabinet_id IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_feeder_source",
                table: "pole",
                sql: "\"feeder_source\" IS NULL OR \"feeder_source\" IN ('verified', 'inferred')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_feeder_source_matches_feeder",
                table: "pole",
                sql: "(feeder_id IS NULL) = (feeder_source IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feeder_cabinet_source",
                table: "feeder",
                sql: "\"cabinet_source\" IS NULL OR \"cabinet_source\" IN ('verified', 'inferred')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feeder_cabinet_source_matches_cabinet",
                table: "feeder",
                sql: "(cabinet_id IS NULL) = (cabinet_source IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_feeder_source",
                table: "pole");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_feeder_source_matches_feeder",
                table: "pole");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feeder_cabinet_source",
                table: "feeder");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feeder_cabinet_source_matches_cabinet",
                table: "feeder");

            migrationBuilder.DropColumn(
                name: "feeder_source",
                table: "pole");

            migrationBuilder.DropColumn(
                name: "cabinet_source",
                table: "feeder");
        }
    }
}
