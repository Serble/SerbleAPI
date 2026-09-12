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
        DbBalance? row = await FindDefault(ownerType, ownerId);
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

    public Task<Balance> SetBalance(BalanceOwnerType ownerType, string ownerId, ulong coins, string? description = null) =>
        Adjust(ownerType, ownerId, row => row.SetCoins(coins), description);

    public Task<Balance> AddCoins(BalanceOwnerType ownerType, string ownerId, ulong amount, string? description = null) =>
        // Credit saturates rather than overflowing.
        Adjust(ownerType, ownerId, row => row.Credit(amount), description);

    public Task<Balance> RemoveCoins(BalanceOwnerType ownerType, string ownerId, ulong amount, string? description = null) =>
        // DebitUpTo clamps at zero: this method's contract is "take what is there". A caller that
        // must not under-charge — a fee, a price — needs a debit that can fail instead, so that the
        // thing being paid for is not handed over for less; see IItemRepository.CreateItemWithFee.
        Adjust(ownerType, ownerId, row => row.DebitUpTo(amount), description);

    /// <summary>
    /// Applies an adjustment to an owner's default balance under the same locking discipline every
    /// other coin movement uses: the row is taken <c>FOR UPDATE</c> inside a serializable
    /// transaction, mutated and saved before the lock is released. Without that, these methods read
    /// a figure and write an absolute value back, so two concurrent calls both compute from the same
    /// starting point and the second silently discards the first.
    /// <para>
    /// The audit record is written in the same save, so a balance never moves without one.
    /// </para>
    /// </summary>
    private Task<Balance> Adjust(
        BalanceOwnerType ownerType, string ownerId, Action<DbBalance> mutate, string? description) =>
        BalanceLocking.Run(db, async () => {
            DbBalance row = await BalanceLocking.LockDefault(db, ownerType, ownerId);
            ulong before = row.Coins;
            mutate(row);
            BalanceLocking.RecordAdjustment(db, row.Id, before, row.Coins, description);
            await db.SaveChangesAsync();
            return Map(row);
        }, _ => true);

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

    /// <summary>
    /// The owner's default (oldest) balance, or null. Read-only in both senses: it never creates a
    /// row, and it never tracks one, so nothing loaded here can be mutated and saved outside the
    /// lock <see cref="Adjust"/> takes.
    /// </summary>
    private Task<DbBalance?> FindDefault(BalanceOwnerType ownerType, string ownerId) {
        int type = (int)ownerType;
        return db.Balances.AsNoTracking()
            .OrderBy(b => b.DateCreated)
            .FirstOrDefaultAsync(b => b.OwnerType == type && b.OwnerId == ownerId);
    }
}
