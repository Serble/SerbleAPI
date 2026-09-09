using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueUsernameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Any username already held by two accounts would make the index below fail, and this
            // migration runs at startup, so that would take the whole API down rather than just
            // this feature. Settle those first: the earliest-created holder keeps the name and the
            // rest are suffixed with their own id, which is unique, so no rename can collide with
            // another name. Partitioning compares under the column collation, the same one the
            // index will enforce, so exactly the rows the index would reject get renamed.
            //
            // This is not undone by Down: the original names are not recorded anywhere.
            migrationBuilder.Sql(
                """
                UPDATE `Users` AS u
                JOIN (
                    SELECT `Id`,
                           ROW_NUMBER() OVER (PARTITION BY `Username` ORDER BY `DateCreated`, `Id`) AS rn
                    FROM `Users`
                ) AS d ON d.`Id` = u.`Id`
                SET u.`Username` = LEFT(CONCAT(LEFT(u.`Username`, 190), '_dup_', u.`Id`), 255)
                WHERE d.rn > 1
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");
        }
    }
}
