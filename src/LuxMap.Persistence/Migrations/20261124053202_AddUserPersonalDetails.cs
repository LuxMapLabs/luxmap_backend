using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPersonalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "app_user",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "app_user",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "app_user",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_number",
                table: "app_user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_type",
                table: "app_user",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "app_user",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_user_identity_type_identity_number",
                table: "app_user",
                columns: new[] { "identity_type", "identity_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_gender",
                table: "app_user",
                sql: "gender IN ('male', 'female', 'other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_identity_pair",
                table: "app_user",
                sql: "(identity_type IS NULL) = (identity_number IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_identity_type",
                table: "app_user",
                sql: "identity_type IN ('citizen_id', 'identity_card', 'passport', 'other')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_app_user_identity_type_identity_number",
                table: "app_user");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_gender",
                table: "app_user");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_identity_pair",
                table: "app_user");

            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_identity_type",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "address",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "identity_number",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "identity_type",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "app_user");
        }
    }
}
