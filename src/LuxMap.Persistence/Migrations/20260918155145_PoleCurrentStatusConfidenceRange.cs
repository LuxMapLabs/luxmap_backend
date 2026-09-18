using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoleCurrentStatusConfidenceRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_current_status_confidence_range",
                table: "pole_current_status",
                sql: "status_confidence IS NULL OR (status_confidence >= 0 AND status_confidence <= 1 AND status_confidence <> 'NaN'::float8 AND status_confidence <> 'Infinity'::float8 AND status_confidence <> '-Infinity'::float8)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_current_status_confidence_range",
                table: "pole_current_status");
        }
    }
}
