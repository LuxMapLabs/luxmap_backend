using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixPrefixedIdOverflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Must come first: the column DEFAULTs below call it.
            //
            // Why a function at all — PostgreSQL's lpad(string, length, fill) TRUNCATES when the
            // string is already longer than `length`, so the previous default handed the 10000th
            // pole `POLE-1000` and collided with the 1000th (23505). Contract section 0.3 requires
            // the ID to simply grow instead. greatest(digits, length(...)) does that but names the
            // value three times, and a column DEFAULT admits neither a subquery nor a CTE — the
            // function is what keeps nextval evaluated exactly ONCE per row.
            //
            // IMMUTABLE is accurate (same arguments, same result) and does not cause folding here:
            // nextval is VOLATILE, so the calling expression stays volatile and runs per row.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION luxmap_format_id(prefix text, value bigint, digits int)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                STRICT
                AS $$
                    SELECT prefix || '-' || lpad(value::text, greatest(digits, length(value::text)), '0')
                $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "segment_id",
                table: "road_segment",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('SEG', nextval('segment_id_seq'), 3)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')");

            migrationBuilder.AlterColumn<string>(
                name: "pole_id",
                table: "pole",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('POLE', nextval('pole_id_seq'), 4)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')");

            migrationBuilder.AlterColumn<string>(
                name: "fixture_id",
                table: "fixture",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('FIX', nextval('fixture_id_seq'), 4)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'FIX-' || LPAD(nextval('fixture_id_seq')::text, 4, '0')");

            migrationBuilder.AlterColumn<string>(
                name: "feeder_id",
                table: "feeder",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('FDR', nextval('feeder_id_seq'), 3)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'FDR-' || LPAD(nextval('feeder_id_seq')::text, 3, '0')");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "app_user",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('USR', nextval('user_id_seq'), 3)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'USR-' || LPAD(nextval('user_id_seq')::text, 3, '0')");

            migrationBuilder.AlterColumn<string>(
                name: "commune_id",
                table: "administrative_unit",
                type: "text",
                nullable: false,
                defaultValueSql: "luxmap_format_id('COM', nextval('commune_id_seq'), 3)",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "'COM-' || LPAD(nextval('commune_id_seq')::text, 3, '0')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "segment_id",
                table: "road_segment",
                type: "text",
                nullable: false,
                defaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('SEG', nextval('segment_id_seq'), 3)");

            migrationBuilder.AlterColumn<string>(
                name: "pole_id",
                table: "pole",
                type: "text",
                nullable: false,
                defaultValueSql: "'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('POLE', nextval('pole_id_seq'), 4)");

            migrationBuilder.AlterColumn<string>(
                name: "fixture_id",
                table: "fixture",
                type: "text",
                nullable: false,
                defaultValueSql: "'FIX-' || LPAD(nextval('fixture_id_seq')::text, 4, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('FIX', nextval('fixture_id_seq'), 4)");

            migrationBuilder.AlterColumn<string>(
                name: "feeder_id",
                table: "feeder",
                type: "text",
                nullable: false,
                defaultValueSql: "'FDR-' || LPAD(nextval('feeder_id_seq')::text, 3, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('FDR', nextval('feeder_id_seq'), 3)");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "app_user",
                type: "text",
                nullable: false,
                defaultValueSql: "'USR-' || LPAD(nextval('user_id_seq')::text, 3, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('USR', nextval('user_id_seq'), 3)");

            migrationBuilder.AlterColumn<string>(
                name: "commune_id",
                table: "administrative_unit",
                type: "text",
                nullable: false,
                defaultValueSql: "'COM-' || LPAD(nextval('commune_id_seq')::text, 3, '0')",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "luxmap_format_id('COM', nextval('commune_id_seq'), 3)");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS luxmap_format_id(text, bigint, int);");
        }
    }
}
