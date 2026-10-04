using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "evidence_id_seq");

            migrationBuilder.CreateTable(
                name: "repair_evidence",
                columns: table => new
                {
                    evidence_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('EVD', nextval('evidence_id_seq'), 4)"),
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    object_key = table.Column<string>(type: "text", nullable: false),
                    thumbnail_key = table.Column<string>(type: "text", nullable: false),
                    byte_count = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repair_evidence", x => x.evidence_id);
                    table.CheckConstraint("ck_repair_evidence_bytes", "byte_count > 0 AND thumbnail_bytes > 0");
                    table.CheckConstraint("ck_repair_evidence_keys_not_blank", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> ''");
                    table.CheckConstraint("ck_repair_evidence_kind", "\"kind\" IN ('before', 'after', 'observation')");
                    table.CheckConstraint("ck_repair_evidence_lat", "lat >= -90 AND lat <= 90");
                    table.CheckConstraint("ck_repair_evidence_lng", "lng >= -180 AND lng <= 180");
                    table.ForeignKey(
                        name: "fk_repair_evidence_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_evidence_app_user_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_evidence_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_commune_id",
                table: "repair_evidence",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_uploaded_by",
                table: "repair_evidence",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_work_order_id_commune_id",
                table: "repair_evidence",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_repair_evidence_uploaded_by_client_op_id",
                table: "repair_evidence",
                columns: new[] { "uploaded_by", "client_op_id" },
                unique: true,
                filter: "client_op_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repair_evidence");

            migrationBuilder.DropSequence(
                name: "evidence_id_seq");
        }
    }
}
