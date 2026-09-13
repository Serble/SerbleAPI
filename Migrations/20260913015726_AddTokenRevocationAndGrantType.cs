using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <summary>
    /// Adds the token cut-off and last-accepted TOTP step to Users, and a grant type to
    /// UserAuthorizedApps. No backfill: null is correct for an account that has revoked no tokens
    /// and spent no step, and GrantType 0 is the legacy flow every existing row came from.
    /// </summary>
    public partial class AddTokenRevocationAndGrantType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpCounter",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidFrom",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantType",
                table: "UserAuthorizedApps",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTotpCounter",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokensValidFrom",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GrantType",
                table: "UserAuthorizedApps");
        }
    }
}
