using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// Server-wide mutual exclusion for tax runs, built on a MySQL advisory lock.
/// <para>
/// The tax background service runs in-process on every replica, so without this two instances
/// would collect concurrently. <c>GET_LOCK</c> is session-scoped, so the lock is taken on the
/// <see cref="SerbleDbContext"/>'s own connection and that connection is pinned open for the whole
/// run — every chunk transaction then rides the same session and stays under the lock.
/// </para>
/// <para>
/// Because connection pooling returns a connection to the pool without ending its session, the
/// lock must be released explicitly; <see cref="DisposeAsync"/> does that and unpins the
/// connection. If the process dies the session dies with it and MySQL frees the lock on our
/// behalf, so a crashed run never wedges the scheduler.
/// </para>
/// <para>
/// This is a liveness optimisation, not the correctness boundary. Scheduled runs are made
/// idempotent by the unique index on <see cref="DbTaxCycle.ScheduledForUtc"/>, which holds even on
/// providers where this lock is a no-op.
/// </para>
/// </summary>
public sealed class TaxRunLock : IAsyncDisposable {
    private const string LockName = "serble.economy.tax.run";

    private readonly SerbleDbContext? _db;

    private TaxRunLock(SerbleDbContext? db) {
        _db = db;
    }

    /// <summary>True when this instance may proceed with a tax run.</summary>
    public bool Acquired { get; private init; }

    /// <summary>
    /// Tries to take the lock without waiting. Returns a non-acquired handle when another instance
    /// already holds it, and an acquired no-op handle on non-MySQL providers (which have no
    /// advisory locks; the unique cycle claim still guards correctness there).
    /// </summary>
    public static async Task<TaxRunLock> TryAcquire(SerbleDbContext db, CancellationToken cancellationToken) {
        if (!IsMySql(db)) return new TaxRunLock(null) { Acquired = true };

        // Pin the connection so every subsequent transaction shares this session, and with it the lock.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try {
            long? result = await ExecuteLockCommand(db, $"SELECT GET_LOCK('{LockName}', 0)", cancellationToken);
            if (result != 1) {
                await db.Database.CloseConnectionAsync();
                return new TaxRunLock(null) { Acquired = false };
            }
        }
        catch {
            await db.Database.CloseConnectionAsync();
            throw;
        }

        return new TaxRunLock(db) { Acquired = true };
    }

    public async ValueTask DisposeAsync() {
        if (_db == null) return;
        try {
            await ExecuteLockCommand(_db, $"SELECT RELEASE_LOCK('{LockName}')", CancellationToken.None);
        }
        finally {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<long?> ExecuteLockCommand(SerbleDbContext db, string sql, CancellationToken cancellationToken) {
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        // GET_LOCK/RELEASE_LOCK are not transactional, but the command must still join any
        // ambient transaction on the connection or MySQL rejects it.
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        object? raw = await command.ExecuteScalarAsync(cancellationToken);
        return raw is null or DBNull ? null : Convert.ToInt64(raw);
    }

    private static bool IsMySql(SerbleDbContext db) =>
        db.Database.IsRelational()
        && db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;
}
