using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class ItemTransactionRepository(SerbleDbContext db) : IItemTransactionRepository {

    private static ItemTransaction Map(DbItemTransaction r) => new() {
        Id            = r.Id,
        ItemId        = r.ItemId,
        Kind          = (ItemTransferKind)r.Kind,
        FromOwnerType = r.FromOwnerType is { } ft ? (BalanceOwnerType)ft : null,
        FromOwnerId   = r.FromOwnerId,
        ToOwnerType   = (BalanceOwnerType)r.ToOwnerType,
        ToOwnerId     = r.ToOwnerId,
        ProposalId    = r.ProposalId,
        DateCreated   = r.DateCreated
    };

    public async Task<ItemTransaction[]> GetHistoryForItem(string itemId, int limit, int offset = 0) {
        List<DbItemTransaction> rows = await db.ItemTransactions.AsNoTracking()
            .Where(t => t.ItemId == itemId)
            .OrderByDescending(t => t.DateCreated)
            .ThenByDescending(t => t.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public Task<int> CountHistoryForItem(string itemId) =>
        db.ItemTransactions.CountAsync(t => t.ItemId == itemId);
}
