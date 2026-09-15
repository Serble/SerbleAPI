using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// Server-wide mutual exclusion built on a MySQL advisory lock, for background work that runs on every replica.
/// <para>
/// <c>GET_LOCK</c> is session-scoped, so the lock is taken on the <see cref="SerbleDbContext"/>'s own
/// connection and that connection is pinned open until <see cref="DisposeAsync"/>, which releases it.
/// If the process dies MySQL frees the lock with the session.
/// </para>
/// </summary>
public sealed class DbAdvisoryLock : IAsyncDisposable {
    private readonly SerbleDbContext? _db;
    private readonly string _name;

    private DbAdvisoryLock(SerbleDbContext? db, string name) {
        _db = db;
        _name = name;
    }

    /// <summary>True when this instance holds the lock.</summary>
    public bool Acquired { get; private init; }

    /// <summary>
    /// Tries to take the lock without waiting. On non-MySQL providers, which have no advisory locks,
    /// this always succeeds.
    /// </summary>
    public static async Task<DbAdvisoryLock> TryAcquire(SerbleDbContext db, string name, CancellationToken cancellationToken) {
        if (!IsMySql(db)) return new DbAdvisoryLock(null, name) { Acquired = true };

        // Pin the connection so every subsequent transaction shares this session, and with it the lock.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try {
            long? result = await ExecuteLockCommand(db, "SELECT GET_LOCK(@name, 0)", name, cancellationToken);
            if (result != 1) {
                await db.Database.CloseConnectionAsync();
                return new DbAdvisoryLock(null, name) { Acquired = false };
            }
        }
        catch {
            await db.Database.CloseConnectionAsync();
            throw;
        }

        return new DbAdvisoryLock(db, name) { Acquired = true };
    }

    public async ValueTask DisposeAsync() {
        if (_db == null) return;
        try {
            await ExecuteLockCommand(_db, "SELECT RELEASE_LOCK(@name)", _name, CancellationToken.None);
        }
        finally {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<long?> ExecuteLockCommand(SerbleDbContext db, string sql, string name,
        CancellationToken cancellationToken) {
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
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
