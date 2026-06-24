using Microsoft.EntityFrameworkCore;
using System.Numerics;
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

    public async Task<Balance> SetBalance(BalanceOwnerType ownerType, string ownerId, ulong coins, string? description = null) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        ulong before = row.Coins;
        row.Coins = coins;
        RecordAdjustment(row.Id, before, row.Coins, description);
        await db.SaveChangesAsync();
        return Map(row);
    }

    public async Task<Balance> AddCoins(BalanceOwnerType ownerType, string ownerId, ulong amount, string? description = null) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        ulong before = row.Coins;
        // Saturate instead of overflowing.
        row.Coins = amount > ulong.MaxValue - row.Coins ? ulong.MaxValue : row.Coins + amount;
        RecordAdjustment(row.Id, before, row.Coins, description);
        await db.SaveChangesAsync();
        return Map(row);
    }

    public async Task<Balance> RemoveCoins(BalanceOwnerType ownerType, string ownerId, ulong amount, string? description = null) {
        DbBalance row = await GetOrCreateDefault(ownerType, ownerId);
        ulong before = row.Coins;
        // Clamp at 0.
        row.Coins = amount >= row.Coins ? 0 : row.Coins - amount;
        RecordAdjustment(row.Id, before, row.Coins, description);
        await db.SaveChangesAsync();
        return Map(row);
    }

    /// <summary>
    /// Records the net change to a balance as an audit transaction so that every balance
    /// mutation is traceable. A net increase is a <em>mint</em> (no source balance,
    /// <c>FromBalanceId == null</c>); a net decrease is a <em>burn</em> (no destination balance,
    /// <c>ToBalanceId == null</c>). No record is written when the balance is unchanged. The row is
    /// added to the change tracker only — it is persisted by the caller's <c>SaveChangesAsync</c>
    /// in the same atomic save as the balance mutation.
    /// </summary>
    private void RecordAdjustment(string balanceId, ulong before, ulong after, string? description) {
        if (after == before) return;
        bool isMint = after > before;
        ulong amount = isMint ? after - before : before - after;
        db.Transactions.Add(new DbTransaction {
            Id            = Guid.NewGuid().ToString(),
            FromBalanceId = isMint ? null : balanceId,
            ToBalanceId   = isMint ? balanceId : null,
            Amount        = amount,
            Description   = description,
            DateCreated   = DateTime.UtcNow
        });
    }

    public Task DeleteBalancesForOwner(BalanceOwnerType ownerType, string ownerId) {
        int type = (int)ownerType;
        return db.Balances.Where(b => b.OwnerType == type && b.OwnerId == ownerId).ExecuteDeleteAsync();
    }

    public async Task<EconomyTotal> GetTotalEconomyValue() {
        // Group + sum per owner type in the database. Coins are summed as decimal (MySQL SUM of
        // an unsigned bigint column) which is then promoted to BigInteger so the grand total can
        // exceed ulong.MaxValue without overflowing.
        var groups = await db.Balances.AsNoTracking()
            .GroupBy(b => b.OwnerType)
            .Select(g => new {
                OwnerType = g.Key,
                Coins = g.Sum(b => (decimal)b.Coins),
                Count = g.LongCount()
            })
            .ToListAsync();

        EconomyTotal result = new();
        foreach (var g in groups) {
            BigInteger sum = new(g.Coins);
            result.TotalCoins += sum;
            result.BalanceCount += g.Count;
            switch ((BalanceOwnerType)g.OwnerType) {
                case BalanceOwnerType.User:
                    result.UserCoins += sum;
                    break;
                case BalanceOwnerType.App:
                    result.AppCoins += sum;
                    break;
            }
        }
        return result;
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
