using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RejectNonFiniteLuxValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lux_reading_value_non_negative",
                table: "lux_reading");

            // `>= 0` alone does NOT reject NaN or Infinity. PostgreSQL orders NaN ABOVE every other
            // float — the opposite of IEEE 754, where every comparison with NaN is false — so both
            // 'NaN' >= 0 and 'Infinity' >= 0 evaluate to TRUE. Verified with a real INSERT before
            // this migration was written; both landed in the table.
            //
            // ⚠️ The NaN test is `<> 'NaN'`, NOT the familiar `x = x`. That idiom belongs to IEEE
            // 754; PostgreSQL treats NaN as EQUAL to itself, so `x = x` is a genuine tautology here
            // and catches nothing. Checked: SELECT 'NaN'::float8 = 'NaN'::float8 returns true.
            migrationBuilder.AddCheckConstraint(
                name: "ck_lux_reading_value_non_negative",
                table: "lux_reading",
                sql: "lux_value >= 0 AND lux_value <> 'NaN'::float8 AND lux_value <> 'Infinity'::float8");

            // The explanation lives in the DATABASE too, not only in this file. Someone reading
            // \d+ or pg_constraint sees three terms where one would seem to do.
            migrationBuilder.Sql("""
                COMMENT ON CONSTRAINT ck_lux_reading_value_non_negative ON lux_reading IS
                  'lux_value >= 0 rejects negatives and -Infinity. <> NaN rejects NaN, which >= 0 '
                  'would otherwise admit because PostgreSQL sorts NaN above all numbers; note it is '
                  'NOT written x = x, since PostgreSQL considers NaN equal to itself and that test '
                  'would pass. <> Infinity rejects +Infinity. lux_value is the ground truth for RQ1 - '
                  'one NaN turns every aggregate CV-12 computes into NaN, silently.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lux_reading_value_non_negative",
                table: "lux_reading");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lux_reading_value_non_negative",
                table: "lux_reading",
                sql: "lux_value >= 0");
        }
    }
}
