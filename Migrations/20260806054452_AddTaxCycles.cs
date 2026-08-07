using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxCycles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsManual = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseRenewedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DynamicRate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(20,10)", nullable: false),
                    BossAppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BossStartingBalance = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    BossEndingBalance = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Collected = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AccountsTaxed = table.Column<int>(type: "int", nullable: false),
                    Distributed = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AppsPaid = table.Column<int>(type: "int", nullable: false),
                    AppsNeedingFunds = table.Column<int>(type: "int", nullable: false),
                    CursorBalanceId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlockedReason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCycles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCycles_ScheduledForUtc",
                table: "TaxCycles",
                column: "ScheduledForUtc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCycles_StartedAt",
                table: "TaxCycles",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCycles_Status",
                table: "TaxCycles",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxCycles");
        }
    }
}
