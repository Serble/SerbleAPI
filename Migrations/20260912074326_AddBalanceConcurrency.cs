using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SerbleAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Any owner that already holds more than one balance would make the unique index below
            // fail, and this migration runs at startup, so that would take the whole API down rather
            // than just this feature. Settle those first by merging them.
            //
            // Merging, not deleting: readers resolve an owner's default balance as their oldest row,
            // so coins credited to any later row are already invisible to every balance read — but
            // they are real coins that were paid for, and they are still being taxed. The oldest row
            // survives and absorbs the rest, and the audit history of the rows that go is repointed
            // at the survivor so the movements stay attached to the owner.
            //
            // Every statement recomputes which row survives from the same window function rather
            // than staging the answer in a temporary table. That is deliberate: CREATE TEMPORARY
            // TABLE needs a privilege the application's database user may not have been granted, and
            // a migration that runs at startup must not be able to fail on a permission. The inputs
            // to the window (Id, OwnerType, OwnerId, DateCreated) are untouched until the final
            // delete, so each recomputation yields the same survivor.
            //
            // This is not undone by Down: which coins came from which row is not recorded anywhere
            // once they are summed.
            migrationBuilder.Sql(
                """
                UPDATE `Balances` AS b
                JOIN (
                    SELECT `Survivor`, SUM(`Coins`) AS `Extra`
                    FROM (
                        SELECT `Coins`, `Id`,
                               FIRST_VALUE(`Id`) OVER (
                                   PARTITION BY `OwnerType`, `OwnerId`
                                   ORDER BY `DateCreated`, `Id`
                               ) AS `Survivor`
                        FROM `Balances`
                    ) AS ranked
                    WHERE `Id` <> `Survivor`
                    GROUP BY `Survivor`
                ) AS m ON m.`Survivor` = b.`Id`
                SET b.`Coins` = CAST(
                    LEAST(CAST(b.`Coins` AS DECIMAL(65,0)) + m.`Extra`, 18446744073709551615)
                    AS UNSIGNED)
                """);

            // Repointed before the rows go: the foreign keys are ON DELETE SET NULL, so deleting
            // first would silently detach the history instead of moving it. A transaction between
            // two rows of the same owner collapses into a self-transfer, which is a faithful record
            // of what it always was.
            migrationBuilder.Sql(
                """
                UPDATE `Transactions` AS t
                JOIN (
                    SELECT `Id` AS `Loser`, `Survivor` FROM (
                        SELECT `Id`,
                               FIRST_VALUE(`Id`) OVER (
                                   PARTITION BY `OwnerType`, `OwnerId`
                                   ORDER BY `DateCreated`, `Id`
                               ) AS `Survivor`
                        FROM `Balances`
                    ) AS ranked
                    WHERE `Id` <> `Survivor`
                ) AS m ON m.`Loser` = t.`FromBalanceId`
                SET t.`FromBalanceId` = m.`Survivor`
                """);

            migrationBuilder.Sql(
                """
                UPDATE `Transactions` AS t
                JOIN (
                    SELECT `Id` AS `Loser`, `Survivor` FROM (
                        SELECT `Id`,
                               FIRST_VALUE(`Id`) OVER (
                                   PARTITION BY `OwnerType`, `OwnerId`
                                   ORDER BY `DateCreated`, `Id`
                               ) AS `Survivor`
                        FROM `Balances`
                    ) AS ranked
                    WHERE `Id` <> `Survivor`
                ) AS m ON m.`Loser` = t.`ToBalanceId`
                SET t.`ToBalanceId` = m.`Survivor`
                """);

            // TaxAppCharges.BalanceId is deliberately left alone. It is per-cycle detail with no
            // foreign key, and its unique (CycleId, BalanceId) would reject a repoint for any cycle
            // that charged two of an owner's rows.
            migrationBuilder.Sql(
                """
                DELETE b FROM `Balances` AS b
                JOIN (
                    SELECT `Id` FROM (
                        SELECT `Id`,
                               FIRST_VALUE(`Id`) OVER (
                                   PARTITION BY `OwnerType`, `OwnerId`
                                   ORDER BY `DateCreated`, `Id`
                               ) AS `Survivor`
                        FROM `Balances`
                    ) AS ranked
                    WHERE `Id` <> `Survivor`
                ) AS m ON m.`Id` = b.`Id`
                """);

            // The schema changes below are written as "only if needed" rather than as plain DDL.
            //
            // MySQL commits implicitly on DDL, so the merge above and these three statements cannot
            // be one transaction no matter what EF wraps around them. If the unique index then fails
            // to build — an old instance still running the previous release can insert a duplicate in
            // the window between the delete and the index — the migration's history row is never
            // written, and the next startup runs the whole thing again. Plain DDL would fail on that
            // replay, because the index it wants to drop is already gone, and the service would
            // crash-loop needing manual repair of the database. Each step checks the catalogue first,
            // so a replay finishes the job instead.
            migrationBuilder.Sql(
                """
                SET @sql := (SELECT IF(
                    EXISTS(SELECT 1 FROM information_schema.statistics
                           WHERE table_schema = DATABASE() AND table_name = 'Balances'
                             AND index_name = 'IX_Balances_OwnerType_OwnerId' AND non_unique = 1),
                    'DROP INDEX `IX_Balances_OwnerType_OwnerId` ON `Balances`',
                    'DO 0'));
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
                """);

            migrationBuilder.Sql(
                """
                SET @sql := (SELECT IF(
                    EXISTS(SELECT 1 FROM information_schema.columns
                           WHERE table_schema = DATABASE() AND table_name = 'Balances'
                             AND column_name = 'RowVersion'),
                    'DO 0',
                    'ALTER TABLE `Balances` ADD COLUMN `RowVersion` bigint unsigned NOT NULL DEFAULT 0'));
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
                """);

            // Unique from here on: a second row for the same owner is a place credits land and are
            // never read again, and the create-on-demand path relies on this index to turn a losing
            // concurrent insert into a no-op instead of a duplicate.
            migrationBuilder.Sql(
                """
                SET @sql := (SELECT IF(
                    EXISTS(SELECT 1 FROM information_schema.statistics
                           WHERE table_schema = DATABASE() AND table_name = 'Balances'
                             AND index_name = 'IX_Balances_OwnerType_OwnerId'),
                    'DO 0',
                    'CREATE UNIQUE INDEX `IX_Balances_OwnerType_OwnerId` ON `Balances` (`OwnerType`, `OwnerId`)'));
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Balances_OwnerType_OwnerId",
                table: "Balances");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Balances");

            migrationBuilder.CreateIndex(
                name: "IX_Balances_OwnerType_OwnerId",
                table: "Balances",
                columns: new[] { "OwnerType", "OwnerId" });
        }
    }
}
