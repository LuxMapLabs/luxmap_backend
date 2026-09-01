using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "feeder_id_seq");

            migrationBuilder.CreateSequence(
                name: "fixture_id_seq");

            migrationBuilder.CreateSequence(
                name: "pole_id_seq");

            migrationBuilder.CreateSequence(
                name: "segment_id_seq");

            migrationBuilder.CreateTable(
                name: "feeder",
                columns: table => new
                {
                    feeder_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'FDR-' || LPAD(nextval('feeder_id_seq')::text, 3, '0')"),
                    feeder_name = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<LineString>(type: "geometry(LineString,4326)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feeder", x => x.feeder_id);
                });

            migrationBuilder.CreateTable(
                name: "road_segment",
                columns: table => new
                {
                    segment_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')"),
                    segment_name = table.Column<string>(type: "text", nullable: false),
                    road_class = table.Column<string>(type: "text", nullable: false),
                    length_m = table.Column<int>(type: "integer", nullable: false),
                    geom = table.Column<LineString>(type: "geometry(LineString,4326)", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_road_segment", x => x.segment_id);
                    table.CheckConstraint("ck_road_segment_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_road_segment_road_class", "\"road_class\" IN ('inter_commune', 'inter_village')");
                });

            migrationBuilder.CreateTable(
                name: "pole",
                columns: table => new
                {
                    pole_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')"),
                    segment_id = table.Column<string>(type: "text", nullable: false),
                    feeder_id = table.Column<string>(type: "text", nullable: true),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    near_sensitive_poi = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pole", x => x.pole_id);
                    table.CheckConstraint("ck_pole_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.ForeignKey(
                        name: "fk_pole_feeder_feeder_id",
                        column: x => x.feeder_id,
                        principalTable: "feeder",
                        principalColumn: "feeder_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pole_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fixture",
                columns: table => new
                {
                    fixture_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'FIX-' || LPAD(nextval('fixture_id_seq')::text, 4, '0')"),
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    fixture_type = table.Column<string>(type: "text", nullable: false),
                    power_source = table.Column<string>(type: "text", nullable: false),
                    lamp_watt = table.Column<int>(type: "integer", nullable: false),
                    install_date = table.Column<DateOnly>(type: "date", nullable: false),
                    removed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    warranty_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    data_source = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixture", x => x.fixture_id);
                    table.CheckConstraint("ck_fixture_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
                    table.CheckConstraint("ck_fixture_fixture_type", "\"fixture_type\" IN ('led_road_lamp', 'solar_all_in_one')");
                    table.CheckConstraint("ck_fixture_power_source", "\"power_source\" IN ('grid', 'solar')");
                    table.ForeignKey(
                        name: "fk_fixture_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pole_current_status",
                columns: table => new
                {
                    pole_id = table.Column<string>(type: "text", nullable: false),
                    fixture_status = table.Column<string>(type: "text", nullable: false),
                    status_confidence = table.Column<double>(type: "double precision", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_sweep_id = table.Column<string>(type: "text", nullable: true),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pole_current_status", x => x.pole_id);
                    table.CheckConstraint("ck_pole_current_status_confidence_matches_status", "(status_confidence IS NULL) = (fixture_status = 'unknown')");
                    table.CheckConstraint("ck_pole_current_status_fixture_status", "\"fixture_status\" IN ('normal', 'dim', 'out', 'unknown')");
                    table.ForeignKey(
                        name: "fk_pole_current_status_pole_pole_id",
                        column: x => x.pole_id,
                        principalTable: "pole",
                        principalColumn: "pole_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feeder_commune_id",
                table: "feeder",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_feeder_geom",
                table: "feeder",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_fixture_commune_id",
                table: "fixture",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixture_pole_id_active",
                table: "fixture",
                column: "pole_id",
                filter: "removed_date IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pole_commune_id",
                table: "pole",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_feeder_id",
                table: "pole",
                column: "feeder_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_geom",
                table: "pole",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_pole_segment_id",
                table: "pole",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_pole_current_status_commune_id",
                table: "pole_current_status",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_road_segment_commune_id",
                table: "road_segment",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_road_segment_geom",
                table: "road_segment",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fixture");

            migrationBuilder.DropTable(
                name: "pole_current_status");

            migrationBuilder.DropTable(
                name: "pole");

            migrationBuilder.DropTable(
                name: "feeder");

            migrationBuilder.DropTable(
                name: "road_segment");

            migrationBuilder.DropSequence(
                name: "feeder_id_seq");

            migrationBuilder.DropSequence(
                name: "fixture_id_seq");

            migrationBuilder.DropSequence(
                name: "pole_id_seq");

            migrationBuilder.DropSequence(
                name: "segment_id_seq");
        }
    }
}
