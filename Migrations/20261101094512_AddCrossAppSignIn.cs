using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossAppSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ViaClientId",
                table: "OidcAuthorizationCodes",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CrossAppAllowedCallers",
                table: "Apps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CrossAppPolicy",
                table: "Apps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CrossAppScopes",
                table: "Apps",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserCrossAppGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CallerAppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetAppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCrossAppGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCrossAppGrants_Apps_CallerAppId",
                        column: x => x.CallerAppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCrossAppGrants_Apps_TargetAppId",
                        column: x => x.TargetAppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCrossAppGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserCrossAppGrants_CallerAppId",
                table: "UserCrossAppGrants",
                column: "CallerAppId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCrossAppGrants_TargetAppId",
                table: "UserCrossAppGrants",
                column: "TargetAppId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCrossAppGrants_UserId_CallerAppId_TargetAppId",
                table: "UserCrossAppGrants",
                columns: new[] { "UserId", "CallerAppId", "TargetAppId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCrossAppGrants");

            migrationBuilder.DropColumn(
                name: "ViaClientId",
                table: "OidcAuthorizationCodes");

            migrationBuilder.DropColumn(
                name: "CrossAppAllowedCallers",
                table: "Apps");

            migrationBuilder.DropColumn(
                name: "CrossAppPolicy",
                table: "Apps");

            migrationBuilder.DropColumn(
                name: "CrossAppScopes",
                table: "Apps");
        }
    }
}
