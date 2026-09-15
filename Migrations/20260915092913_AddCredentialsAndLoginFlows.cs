using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <summary>
    /// Moves passwords, TOTP secrets and passkeys into UserCredentials and gives every account the
    /// sign-in flows that match how it signs in today. Users.Password is left for
    /// PasswordHashUpgradeService to clear once each hash is wrapped. The data statements skip rows
    /// already copied, so a partly applied migration can be re-run after dropping the new tables and
    /// the UserPasskeys.UserCredentialId column.
    /// </summary>
    public partial class AddCredentialsAndLoginFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserCredentialId",
                table: "UserPasskeys",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LoginSessions",
                columns: table => new
                {
                    IdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    CompletedMask = table.Column<int>(type: "int", nullable: false),
                    PasskeyChallenge = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Consumed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginSessions", x => x.IdHash);
                    table.ForeignKey(
                        name: "FK_LoginSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserCredentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Secret = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scheme = table.Column<int>(type: "int", nullable: false),
                    LegacySalt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Counter = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserLoginFlows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MethodMask = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLoginFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLoginFlows_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Types: 1 password, 2 TOTP, 3 passkey. Flow bits are 1 << type: 2, 4, 8.
            migrationBuilder.Sql("""
                INSERT INTO UserCredentials (Id, UserId, Type, Name, Status, Secret, Scheme, LegacySalt, Counter, CreatedAt, LastUsedAt)
                SELECT UUID(), u.Id, 1, NULL, 1, u.Password, 1, COALESCE(u.PasswordSalt, ''), NULL, COALESCE(u.DateCreated, UTC_TIMESTAMP(6)), NULL
                FROM Users u
                WHERE u.Password IS NOT NULL AND u.Password <> ''
                  AND NOT EXISTS (SELECT 1 FROM UserCredentials c WHERE c.UserId = u.Id AND c.Type = 1);
                """);

            migrationBuilder.Sql("""
                INSERT INTO UserCredentials (Id, UserId, Type, Name, Status, Secret, Scheme, LegacySalt, Counter, CreatedAt, LastUsedAt)
                SELECT UUID(), u.Id, 2, NULL, 1, u.TotpSecret, 1, NULL, u.LastTotpCounter, COALESCE(u.DateCreated, UTC_TIMESTAMP(6)), NULL
                FROM Users u
                WHERE u.TotpEnabled = 1 AND u.TotpSecret IS NOT NULL AND u.TotpSecret <> ''
                  AND NOT EXISTS (SELECT 1 FROM UserCredentials c WHERE c.UserId = u.Id AND c.Type = 2);
                """);

            migrationBuilder.Sql("UPDATE UserPasskeys SET UserCredentialId = UUID() WHERE UserCredentialId IS NULL;");

            migrationBuilder.Sql("""
                INSERT INTO UserCredentials (Id, UserId, Type, Name, Status, Secret, Scheme, LegacySalt, Counter, CreatedAt, LastUsedAt)
                SELECT p.UserCredentialId, p.OwnerId, 3, LEFT(p.Name, 255), 1, NULL, 0, NULL, NULL, COALESCE(p.CreatedAt, UTC_TIMESTAMP(6)), NULL
                FROM UserPasskeys p
                WHERE NOT EXISTS (SELECT 1 FROM UserCredentials c WHERE c.Id = p.UserCredentialId);
                """);

            migrationBuilder.Sql("""
                INSERT INTO UserLoginFlows (Id, UserId, MethodMask, CreatedAt)
                SELECT UUID(), u.Id,
                       2 | IF(EXISTS (SELECT 1 FROM UserCredentials t WHERE t.UserId = u.Id AND t.Type = 2), 4, 0),
                       UTC_TIMESTAMP(6)
                FROM Users u
                WHERE EXISTS (SELECT 1 FROM UserCredentials c WHERE c.UserId = u.Id AND c.Type = 1)
                  AND NOT EXISTS (SELECT 1 FROM UserLoginFlows f WHERE f.UserId = u.Id AND (f.MethodMask & 2) <> 0);
                """);

            migrationBuilder.Sql("""
                INSERT INTO UserLoginFlows (Id, UserId, MethodMask, CreatedAt)
                SELECT UUID(), u.Id, 8, UTC_TIMESTAMP(6)
                FROM Users u
                WHERE EXISTS (SELECT 1 FROM UserCredentials c WHERE c.UserId = u.Id AND c.Type = 3)
                  AND NOT EXISTS (SELECT 1 FROM UserLoginFlows f WHERE f.UserId = u.Id AND f.MethodMask = 8);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserCredentialId",
                table: "UserPasskeys",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_UserCredentialId",
                table: "UserPasskeys",
                column: "UserCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_ExpiresAt",
                table: "LoginSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_UserId",
                table: "LoginSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCredentials_UserId_Type",
                table: "UserCredentials",
                columns: new[] { "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginFlows_UserId_MethodMask",
                table: "UserLoginFlows",
                columns: new[] { "UserId", "MethodMask" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPasskeys_UserCredentials_UserCredentialId",
                table: "UserPasskeys",
                column: "UserCredentialId",
                principalTable: "UserCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "AddCredentialsAndLoginFlows cannot be reverted: credentials created since it ran exist nowhere else.");
        }
    }
}
