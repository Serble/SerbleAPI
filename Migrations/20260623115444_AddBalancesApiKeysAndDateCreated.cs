using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBalancesApiKeysAndDateCreated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreated",
                table: "Users",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "(UTC_TIMESTAMP())");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreated",
                table: "UserAuthorizedApps",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "(UTC_TIMESTAMP())");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateCreated",
                table: "Apps",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "(UTC_TIMESTAMP())");

            migrationBuilder.CreateTable(
                name: "AppApiKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyPrefix = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppApiKeys_Apps_AppId",
                        column: x => x.AppId,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Balances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Coins = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Balances", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppApiKeys_AppId",
                table: "AppApiKeys",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_AppApiKeys_KeyHash",
                table: "AppApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Balances_OwnerType_OwnerId",
                table: "Balances",
                columns: new[] { "OwnerType", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppApiKeys");

            migrationBuilder.DropTable(
                name: "Balances");

            migrationBuilder.DropColumn(
                name: "DateCreated",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DateCreated",
                table: "UserAuthorizedApps");

            migrationBuilder.DropColumn(
                name: "DateCreated",
                table: "Apps");
        }
    }
}
