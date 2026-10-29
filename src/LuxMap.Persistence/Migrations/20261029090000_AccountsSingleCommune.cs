using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations;

/// <inheritdoc />
public partial class AccountsSingleCommune : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the earliest assignment; C collation makes tied IDs deterministic and ordinal.
        migrationBuilder.Sql("""
            DO $$
            DECLARE removed record;
            BEGIN
                FOR removed IN
                    WITH ranked AS (
                        SELECT user_id, commune_id,
                            row_number() OVER (
                                PARTITION BY user_id ORDER BY assigned_at, commune_id COLLATE "C"
                            ) AS position
                        FROM app_user_commune
                    )
                    DELETE FROM app_user_commune AS assignment
                    USING ranked
                    WHERE ranked.position > 1
                        AND assignment.user_id = ranked.user_id
                        AND assignment.commune_id = ranked.commune_id
                    RETURNING assignment.user_id, assignment.commune_id
                LOOP
                    RAISE NOTICE 'AccountsSingleCommune removed (user_id=%, commune_id=%)',
                        removed.user_id, removed.commune_id;
                END LOOP;
            END $$;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_app_user_commune_user_id",
            table: "app_user_commune",
            column: "user_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deleted assignments cannot be restored; restore a pre-deployment backup if needed.
        migrationBuilder.DropIndex(name: "ux_app_user_commune_user_id", table: "app_user_commune");
    }
}
