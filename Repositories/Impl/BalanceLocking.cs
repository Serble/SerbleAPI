using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

/// <summary>
/// The locking discipline every coin movement shares.
///
/// <para><b>Why it exists.</b> A balance is changed by reading it, checking it, and writing an
/// absolute value back. Done without a lock, two requests read the same starting figure, both pass
/// the funds check, and the second write overwrites the first — one debit, two credits, coins from
/// nothing. Clamping or saturating the arithmetic does not help, because the lost write is the bug,
/// not the overflow. The only fix is to hold the row from the read until the write lands, which is
/// what <see cref="LockDefault"/> and <see cref="Run{T}"/> do together.</para>
///
/// <para><b>The discipline.</b> Take every balance the operation will touch with
/// <c>SELECT … FOR UPDATE</c> before checking any of them, in one serializable transaction, and
/// write inside it. Locks are acquired in a fixed order (see <see cref="LockDefaults"/>) so two
/// operations touching the same pair cannot each hold what the other needs. Losing that race is
/// recoverable rather than fatal: <see cref="Run{T}"/> retries a transaction the database rolled
/// back for a deadlock, a lock-wait timeout or a stale concurrency token, so an operation fails
/// only when it is genuinely impossible, not because it was unlucky.</para>
///
/// <para><b>Non-relational providers</b> (in-memory, used by tests) have neither row locks nor
/// transactions. There, the helpers degrade to plain tracked reads and a single save — the
/// semantics stay correct for sequential use, which is all such a provider offers.</para>
/// </summary>
internal static class BalanceLocking {
    /// <summary>
    /// How many times a rolled-back attempt is replayed. Deadlocks are resolved by the database
    /// killing one side immediately, so a handful of attempts is plenty; the cap is there so a
    /// permanently conflicting workload surfaces an error instead of spinning.
    /// </summary>
    private const int MaxAttempts = 5;

    /// <summary>
    /// Runs <paramref name="attempt"/> inside one serializable transaction, committing only when
    /// <paramref name="shouldCommit"/> accepts the result, and replaying the whole attempt if the
    /// database rolled it back for a transient reason.
    /// <para>
    /// The change tracker is cleared before each attempt: a replay must re-read the rows it is
    /// about to change, and entities left over from the rolled-back attempt would otherwise be
    /// written again with their pre-rollback values. Callers must therefore treat every entity
    /// loaded inside <paramref name="attempt"/> as belonging to that attempt alone.
    /// </para>
    /// </summary>
    internal static async Task<T> Run<T>(
        SerbleDbContext db,
        Func<Task<T>> attempt,
        Func<T, bool> shouldCommit,
        CancellationToken cancellationToken = default) {

        if (!db.Database.IsRelational()) return await attempt();

        for (int attemptNumber = 1; ; attemptNumber++) {
            db.ChangeTracker.Clear();
            IDbContextTransaction tx = await db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try {
                T result = await attempt();
                // A rejected outcome is not an error, but nothing it touched should persist: the
                // balance row it created on demand, for instance, is only wanted if the move
                // happened. Falling through without committing rolls it back on dispose.
                if (shouldCommit(result)) await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (attemptNumber < MaxAttempts && IsRetryable(ex)) {
                // Rolled back by the database (or by our own concurrency token). Nothing was
                // written, so the operation can simply be re-read and re-tried.
            }
            finally {
                await SafeDispose(tx);
            }
        }
    }

    /// <summary>
    /// Whether the failure means "the transaction was rolled back, try again" rather than "this
    /// operation is invalid". Matching on <see cref="System.Data.Common.DbException.IsTransient"/> and the ANSI
    /// serialization-failure SQLSTATE keeps this independent of which provider is behind EF, the
    /// same reason the username-collision path asks the table instead of reading an error code.
    /// </summary>
    private static bool IsRetryable(Exception exception) {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException) {
            // Our own row-version check: someone changed the balance between the read and the
            // write, so the read has to be redone.
            if (ex is DbUpdateConcurrencyException) return true;
            if (ex is System.Data.Common.DbException dbEx && (dbEx.IsTransient || dbEx.SqlState == "40001")) return true;
        }
        return false;
    }

    /// <summary>
    /// Disposes the transaction without letting a rollback failure replace the error that caused
    /// it. A transaction the server already aborted can refuse a second rollback, and that refusal
    /// says nothing a caller can act on.
    /// </summary>
    private static async Task SafeDispose(IDbContextTransaction tx) {
        try {
            await tx.DisposeAsync();
        }
        catch {
            // Intentionally swallowed: see above.
        }
    }

    /// <summary>
    /// Locks every owner's default balance and returns them keyed by owner, creating rows that do
    /// not exist yet. Duplicate owners collapse to one lock and one row.
    /// <para>
    /// Locks are taken in a fixed order derived from the owner itself, so two operations moving
    /// coins in opposite directions between the same two parties queue behind each other instead of
    /// deadlocking. (A deadlock against the tax collector, which walks the table in primary-key
    /// order, is still possible; <see cref="Run{T}"/> retries it.)
    /// </para>
    /// </summary>
    internal static async Task<Dictionary<(BalanceOwnerType type, string id), DbBalance>> LockDefaults(
        SerbleDbContext db,
        IEnumerable<(BalanceOwnerType type, string id)> owners,
        CancellationToken cancellationToken = default) {

        (BalanceOwnerType type, string id)[] ordered = owners
            .Distinct()
            .OrderBy(o => (int)o.type)
            .ThenBy(o => o.id, StringComparer.Ordinal)
            .ToArray();

        Dictionary<(BalanceOwnerType, string), DbBalance> locked = new(ordered.Length);
        foreach ((BalanceOwnerType type, string id) owner in ordered) {
            locked[owner] = await LockDefault(db, owner.type, owner.id, cancellationToken);
        }
        return locked;
    }

    /// <summary>
    /// Returns the owner's default (oldest) balance tracked and locked for update, creating the row
    /// if none exists. Must be called inside a transaction — see <see cref="Run{T}"/>.
    /// </summary>
    internal static async Task<DbBalance> LockDefault(
        SerbleDbContext db, BalanceOwnerType ownerType, string ownerId,
        CancellationToken cancellationToken = default) {

        int type = (int)ownerType;

        // A row created earlier in this same unit of work is not in the database yet, so the
        // locking select cannot see it. Reuse that instance rather than inserting a second row for
        // the same owner, which the unique index would reject.
        DbBalance? pending = db.Balances.Local
            .FirstOrDefault(b => b.OwnerType == type && b.OwnerId == ownerId
                                 && db.Entry(b).State == EntityState.Added);
        if (pending != null) return pending;

        DbBalance? row = await SelectDefaultForUpdate(db, ownerType, ownerId, cancellationToken);
        if (row != null) return row;

        if (db.Database.IsRelational()) {
            // Create-if-absent in one statement: a concurrent creation makes this a no-op instead
            // of a duplicate-key failure, and the row is locked by the select that follows. This is
            // the half of the fix the unique index cannot provide on its own.
            await InsertIfAbsent(db, ownerType, ownerId, cancellationToken);
            row = await SelectDefaultForUpdate(db, ownerType, ownerId, cancellationToken);
            if (row != null) return row;
        }

        DbBalance created = NewRow(ownerType, ownerId);
        db.Balances.Add(created);
        return created;
    }

    private static DbBalance NewRow(BalanceOwnerType ownerType, string ownerId) => new() {
        Id          = Guid.NewGuid().ToString(),
        OwnerType   = (int)ownerType,
        OwnerId     = ownerId,
        DateCreated = DateTime.UtcNow
    };

    private static async Task<DbBalance?> SelectDefaultForUpdate(
        SerbleDbContext db, BalanceOwnerType ownerType, string ownerId,
        CancellationToken cancellationToken) {

        int type = (int)ownerType;
        if (!db.Database.IsRelational()) {
            return await db.Balances
                .OrderBy(b => b.DateCreated)
                .FirstOrDefaultAsync(b => b.OwnerType == type && b.OwnerId == ownerId, cancellationToken);
        }

        // Ordered by DateCreated so the row taken is the same one readers resolve as the owner's
        // default, for any owner that still has more than one from before the unique index.
        string sql =
            "SELECT * FROM `Balances` WHERE `OwnerType` = {0} AND `OwnerId` = {1} " +
            "ORDER BY `DateCreated` LIMIT 1 FOR UPDATE";
        List<DbBalance> rows = await db.Balances
            .FromSqlRaw(sql, type, ownerId)
            .ToListAsync(cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static Task InsertIfAbsent(
        SerbleDbContext db, BalanceOwnerType ownerType, string ownerId,
        CancellationToken cancellationToken) {

        // `Id` = `Id` is the cheapest way to say "do nothing on conflict" while still reporting the
        // row as present; the unique index on (OwnerType, OwnerId) is what the conflict fires on.
        string sql =
            "INSERT INTO `Balances` (`Id`, `OwnerType`, `OwnerId`, `Coins`, `RowVersion`, `DateCreated`) " +
            "VALUES ({0}, {1}, {2}, 0, 0, {3}) " +
            "ON DUPLICATE KEY UPDATE `Id` = `Id`";
        return db.Database.ExecuteSqlRawAsync(
            sql,
            [Guid.NewGuid().ToString(), (int)ownerType, ownerId, DateTime.UtcNow],
            cancellationToken);
    }

    /// <summary>
    /// Records a net change to a single balance as an audit transaction so that every coin movement
    /// is traceable. A net increase is a <em>mint</em> (no source balance) and a net decrease a
    /// <em>burn</em> (no destination). Nothing is written when the balance is unchanged. The row is
    /// added to the change tracker only — it is persisted by the caller's save, inside the same
    /// transaction as the balance mutation.
    /// </summary>
    internal static void RecordAdjustment(
        SerbleDbContext db, string balanceId, ulong before, ulong after, string? description) {

        if (after == before) return;
        bool isMint = after > before;
        db.Transactions.Add(new DbTransaction {
            Id            = Guid.NewGuid().ToString(),
            FromBalanceId = isMint ? null : balanceId,
            ToBalanceId   = isMint ? balanceId : null,
            Amount        = isMint ? after - before : before - after,
            Description   = description,
            DateCreated   = DateTime.UtcNow
        });
    }
}
