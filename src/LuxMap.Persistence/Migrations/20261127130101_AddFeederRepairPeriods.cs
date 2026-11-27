using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeederRepairPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feeder_repair_period",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    feeder_id = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feeder_repair_period", x => x.id);
                    table.CheckConstraint("ck_feeder_repair_period_time", "ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "fk_feeder_repair_period_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feeder_repair_period_feeder_feeder_id_commune_id",
                        columns: x => new { x.feeder_id, x.commune_id },
                        principalTable: "feeder",
                        principalColumns: new[] { "feeder_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feeder_repair_period_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_commune_id",
                table: "feeder_repair_period",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_feeder_id",
                table: "feeder_repair_period",
                column: "feeder_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_feeder_id_commune_id",
                table: "feeder_repair_period",
                columns: new[] { "feeder_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_feeder_id_started_at",
                table: "feeder_repair_period",
                columns: new[] { "feeder_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_work_order_id_commune_id",
                table: "feeder_repair_period",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_repair_period_work_order_id_started_at",
                table: "feeder_repair_period",
                columns: new[] { "work_order_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feeder_repair_period");
        }
    }
}
