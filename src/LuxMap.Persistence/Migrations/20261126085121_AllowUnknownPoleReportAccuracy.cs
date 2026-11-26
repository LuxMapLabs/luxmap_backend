using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowUnknownPoleReportAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_pole_target_report_complete",
                table: "work_order_pole_target");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_pole_target_report_complete",
                table: "work_order_pole_target",
                sql: "(reported_lat IS NULL AND reported_lng IS NULL AND reported_accuracy_m IS NULL AND reported_captured_at IS NULL)\nOR (reported_lat IS NOT NULL AND reported_lng IS NOT NULL AND reported_captured_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails while a report has accuracy_m NULL (PG-14). Those reports are real data: decide what they become before rolling back.
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_pole_target_report_complete",
                table: "work_order_pole_target");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_pole_target_report_complete",
                table: "work_order_pole_target",
                sql: "(reported_lat IS NULL AND reported_lng IS NULL AND reported_accuracy_m IS NULL AND reported_captured_at IS NULL)\nOR (reported_lat IS NOT NULL AND reported_lng IS NOT NULL AND reported_accuracy_m IS NOT NULL AND reported_captured_at IS NOT NULL)");
        }
    }
}
