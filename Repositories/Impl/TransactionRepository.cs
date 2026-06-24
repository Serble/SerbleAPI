using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class TransactionRepository(SerbleDbContext db) : ITransactionRepository {

    private static Transaction Map(DbTransaction r) => new() {
        Id            = r.Id,
        FromBalanceId = r.FromBalanceId,
        ToBalanceId   = r.ToBalanceId,
        Amount        = r.Amount,
        Description   = r.Description,
        DateCreated   = r.DateCreated
    };

    private static Balance MapBalance(DbBalance r) => new() {
        Id          = r.Id,
        OwnerType   = (BalanceOwnerType)r.OwnerType,
        OwnerId     = r.OwnerId,
        Coins       = r.Coins,
        DateCreated = r.DateCreated
    };

    public async Task<TransferOutcome> Transfer(
        BalanceOwnerType fromType, string fromId,
        BalanceOwnerType toType, string toId,
        ulong amount, string? description) {

        if (amount == 0) return TransferOutcome.Fail(TransferError.ZeroAmount);
        if (fromType == toType && fromId == toId) return TransferOutcome.Fail(TransferError.SameOwner);

        DbBalance from = await GetOrCreateDefault(fromType, fromId);
        DbBalance to = await GetOrCreateDefault(toType, toId);

        if (from.Coins < amount) return TransferOutcome.Fail(TransferError.InsufficientFunds);
        if (to.Coins > ulong.MaxValue - amount) return TransferOutcome.Fail(TransferError.RecipientOverflow);

        // Zero-sum: deduct from sender, add to receiver, record the movement — one atomic save.
        from.Coins -= amount;
        to.Coins += amount;

        DbTransaction tx = new() {
            Id            = Guid.NewGuid().ToString(),
            FromBalanceId = from.Id,
            ToBalanceId   = to.Id,
            Amount        = amount,
            Description   = description,
            DateCreated   = DateTime.UtcNow
        };
        db.Transactions.Add(tx);

        await db.SaveChangesAsync();
        return TransferOutcome.Ok(Map(tx), MapBalance(from), MapBalance(to));
    }

    public async Task<Transaction[]> GetTransactionsForBalance(string balanceId, int limit, int offset = 0) {
        List<DbTransaction> rows = await db.Transactions.AsNoTracking()
            .Where(t => t.FromBalanceId == balanceId || t.ToBalanceId == balanceId)
            .OrderByDescending(t => t.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Transaction[]> GetTransactionsForOwner(BalanceOwnerType ownerType, string ownerId, int limit, int offset = 0) {
        int type = (int)ownerType;
        List<string> balanceIds = await db.Balances.AsNoTracking()
            .Where(b => b.OwnerType == type && b.OwnerId == ownerId)
            .Select(b => b.Id)
            .ToListAsync();
        if (balanceIds.Count == 0) return [];

        List<DbTransaction> rows = await db.Transactions.AsNoTracking()
            .Where(t => (t.FromBalanceId != null && balanceIds.Contains(t.FromBalanceId))
                     || (t.ToBalanceId != null && balanceIds.Contains(t.ToBalanceId)))
            .OrderByDescending(t => t.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Transaction[]> QueryTransactions(
        (BalanceOwnerType type, string id)? any,
        (BalanceOwnerType type, string id)? from,
        (BalanceOwnerType type, string id)? to,
        int limit, int offset) {

        IQueryable<DbTransaction> q = db.Transactions.AsNoTracking();

        if (from is { } f) {
            List<string> ids = await BalanceIdsFor(f.type, f.id);
            q = q.Where(t => t.FromBalanceId != null && ids.Contains(t.FromBalanceId));
        }
        if (to is { } tt) {
            List<string> ids = await BalanceIdsFor(tt.type, tt.id);
            q = q.Where(t => t.ToBalanceId != null && ids.Contains(t.ToBalanceId));
        }
        if (any is { } a) {
            List<string> ids = await BalanceIdsFor(a.type, a.id);
            q = q.Where(t => (t.FromBalanceId != null && ids.Contains(t.FromBalanceId))
                          || (t.ToBalanceId != null && ids.Contains(t.ToBalanceId)));
        }

        List<DbTransaction> rows = await q
            .OrderByDescending(t => t.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    private Task<List<string>> BalanceIdsFor(BalanceOwnerType ownerType, string ownerId) {
        int type = (int)ownerType;
        return db.Balances.AsNoTracking()
            .Where(b => b.OwnerType == type && b.OwnerId == ownerId)
            .Select(b => b.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the owner's default (oldest) balance tracked, creating it if none exists. The
    /// new row's id is assigned immediately so it can be referenced by a transaction within the
    /// same save.
    /// </summary>
    private async Task<DbBalance> GetOrCreateDefault(BalanceOwnerType ownerType, string ownerId) {
        int type = (int)ownerType;
        DbBalance? row = await db.Balances
            .OrderBy(b => b.DateCreated)
            .FirstOrDefaultAsync(b => b.OwnerType == type && b.OwnerId == ownerId);
        if (row != null) return row;

        row = new DbBalance {
            Id = Guid.NewGuid().ToString(),
            OwnerType = type,
            OwnerId = ownerId,
            Coins = 0,
            DateCreated = DateTime.UtcNow
        };
        db.Balances.Add(row);
        return row;
    }
}
