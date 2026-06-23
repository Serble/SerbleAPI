using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class BalanceRepository(SerbleDbContext db) : IBalanceRepository {

    private static Balance Map(DbBalance r) => new() {
        Id          = r.Id,
        OwnerType   = (BalanceOwnerType)r.OwnerType,
        OwnerId     = r.OwnerId,
        Coins       = r.Coins,
        DateCreated = r.DateCreated
    };

    public async Task<Balance> GetBalance(BalanceOwnerType ownerType, string ownerId) {
        DbBalance? row = await FindDefault(ownerType, ownerId, tracking: false);
        if (row != null) return Map(row);
        // Transient zero balance — not persisted on read.
        return new Balance {
            Id = "",
            OwnerType = ownerType,
            OwnerId = ownerId,
            Coins = 0,
            DateCreated = default
        };
    }

    public async Task<Balance?> GetBalanceById(string balanceId) {
        DbBalance? row = await db.Balances.AsNoTracking().FirstOrDefaultAsync(b => b.Id == balanceId);
        return row == null ? null : Map(row);
    }

    public async Task<Balance[]> GetBalancesByIds(IEnumerable<string> ids) {
        string[] arr = ids.Distinct().ToArray();
        if (arr.Length == 0) return [];
        List<DbBalance> rows = await db.Balances.AsNoTracking()
            .Where(b => arr.Contains(b.Id)).ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Balance> SetBalance(BalanceOwnerType ownerType, string ownerId, ulong coins) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        row.Coins = coins;
        await db.SaveChangesAsync();
        return Map(row);
    }

    public async Task<Balance> AddCoins(BalanceOwnerType ownerType, string ownerId, ulong amount) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        // Saturate instead of overflowing.
        row.Coins = amount > ulong.MaxValue - row.Coins ? ulong.MaxValue : row.Coins + amount;
        await db.SaveChangesAsync();
        return Map(row);
    }

    public async Task<Balance> RemoveCoins(BalanceOwnerType ownerType, string ownerId, ulong amount) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        // Clamp at 0.
        row.Coins = amount >= row.Coins ? 0 : row.Coins - amount;
        await db.SaveChangesAsync();
        return Map(row);
    }

    public Task DeleteBalancesForOwner(BalanceOwnerType ownerType, string ownerId) {
        int type = (int)ownerType;
        return db.Balances.Where(b => b.OwnerType == type && b.OwnerId == ownerId).ExecuteDeleteAsync();
    }

    private Task<DbBalance?> FindDefault(BalanceOwnerType ownerType, string ownerId, bool tracking) {
        int type = (int)ownerType;
        IQueryable<DbBalance> q = tracking ? db.Balances : db.Balances.AsNoTracking();
        return q.OrderBy(b => b.DateCreated)
            .FirstOrDefaultAsync(b => b.OwnerType == type && b.OwnerId == ownerId);
    }

    private async Task<DbBalance> GetOrCreateDefault(BalanceOwnerType ownerType, string ownerId) {
        DbBalance? row = await FindDefault(ownerType, ownerId, tracking: true);
        if (row != null) return row;
        row = new DbBalance {
            Id = Guid.NewGuid().ToString(),
            OwnerType = (int)ownerType,
            OwnerId = ownerId,
            Coins = 0,
            DateCreated = DateTime.UtcNow
        };
        db.Balances.Add(row);
        return row;
    }
}
