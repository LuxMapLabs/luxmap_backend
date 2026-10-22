using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "consecutive_failures",
                table: "scheduled_job",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_failed_at",
                table: "scheduled_job",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "storage_alert",
                columns: table => new
                {
                    alert_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    free_ratio_at_open = table.Column<double>(type: "double precision", nullable: false),
                    notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_alert", x => x.alert_id);
                    table.CheckConstraint("ck_storage_alert_order", "(notified_at IS NULL OR notified_at >= opened_at) AND (closed_at IS NULL OR closed_at >= opened_at)");
                    table.CheckConstraint("ck_storage_alert_ratio", "free_ratio_at_open >= 0 AND free_ratio_at_open <= 1 AND free_ratio_at_open <> 'NaN'::float8");
                });

            migrationBuilder.CreateTable(
                name: "storage_reconciliation",
                columns: table => new
                {
                    bucket = table.Column<string>(type: "text", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    store_object_count = table.Column<long>(type: "bigint", nullable: false),
                    store_bytes = table.Column<long>(type: "bigint", nullable: false),
                    orphan_count = table.Column<long>(type: "bigint", nullable: false),
                    orphan_bytes = table.Column<long>(type: "bigint", nullable: false),
                    missing_count = table.Column<long>(type: "bigint", nullable: false),
                    orphan_sample = table.Column<string[]>(type: "text[]", nullable: false),
                    missing_sample = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_reconciliation", x => x.bucket);
                    table.CheckConstraint("ck_storage_reconciliation_bucket", "\"bucket\" IN ('survey', 'evidence', 'video', 'citizen')");
                    table.CheckConstraint("ck_storage_reconciliation_counts", "store_object_count >= 0 AND store_bytes >= 0 AND orphan_count >= 0 AND orphan_bytes >= 0 AND missing_count >= 0 AND orphan_count <= store_object_count AND orphan_bytes <= store_bytes");
                    table.CheckConstraint("ck_storage_reconciliation_samples", "cardinality(orphan_sample) <= LEAST(orphan_count, 20) AND cardinality(missing_sample) <= LEAST(missing_count, 20)");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_scheduled_job_consecutive_failures",
                table: "scheduled_job",
                sql: "consecutive_failures >= 0 AND (consecutive_failures = 0 OR last_failed_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_storage_alert_open",
                table: "storage_alert",
                column: "closed_at",
                unique: true,
                filter: "closed_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_alert");

            migrationBuilder.DropTable(
                name: "storage_reconciliation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_scheduled_job_consecutive_failures",
                table: "scheduled_job");

            migrationBuilder.DropColumn(
                name: "consecutive_failures",
                table: "scheduled_job");

            migrationBuilder.DropColumn(
                name: "last_failed_at",
                table: "scheduled_job");
        }
    }
}
