using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class ItemRepository(SerbleDbContext db) : IItemRepository {

    private static Item Map(DbItem r) => new() {
        Id           = r.Id,
        OwnerType    = (BalanceOwnerType)r.OwnerType,
        OwnerId      = r.OwnerId,
        CreatorAppId = r.CreatorAppId,
        DateCreated  = r.DateCreated,
        Name         = r.Name,
        Description  = r.Description,
        IconUrl      = r.IconUrl
    };

    public async Task CreateItem(Item item) {
        Track(item);
        await db.SaveChangesAsync();
    }

    public async Task<ItemCreationOutcome> CreateItemWithFee(Item item, ulong fee, string? feeDescription) {
        if (fee == 0) {
            await CreateItem(item);
            return ItemCreationOutcome.Ok();
        }

        // The fee and the item are one transaction, and the debit happens before the item exists:
        // charging afterwards lets concurrent creations all pass the same affordability check and
        // mint an item each, and a clamped debit then burns whatever is left instead of the fee. A
        // debit that cannot be paid in full rolls the item back with it.
        return await BalanceLocking.Run(db, async () => {
            DbBalance balance = await BalanceLocking.LockDefault(db, item.OwnerType, item.OwnerId);
            ulong before = balance.Coins;
            if (!balance.TryDebit(fee)) return ItemCreationOutcome.Fail(ItemCreationError.InsufficientFunds);
            BalanceLocking.RecordAdjustment(db, balance.Id, before, balance.Coins, feeDescription);
            Track(item);
            await db.SaveChangesAsync();
            return ItemCreationOutcome.Ok();
        }, outcome => outcome.Success);
    }

    /// <summary>
    /// Adds a new item and its genesis ownership record to the change tracker without saving, so
    /// that a caller can persist both in the same transaction as something else — the creation fee,
    /// for instance.
    /// </summary>
    private void Track(Item item) {
        db.Items.Add(new DbItem {
            Id           = item.Id,
            OwnerType    = (int)item.OwnerType,
            OwnerId      = item.OwnerId,
            CreatorAppId = item.CreatorAppId,
            DateCreated  = item.DateCreated,
            Name         = item.Name,
            Description  = item.Description,
            IconUrl      = item.IconUrl
        });
        // Genesis entry in the item's ownership history: minted into its creator's ownership.
        db.ItemTransactions.Add(
            DbItemTransaction.Created(item.Id, item.OwnerType, item.OwnerId, item.DateCreated));
    }

    public async Task<Item?> GetItem(string id) {
        DbItem? row = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        return row == null ? null : Map(row);
    }

    public async Task<Item[]> GetItemsForOwner(BalanceOwnerType ownerType, string ownerId, int limit, int offset = 0,
        string? creatorAppId = null, string? search = null) {
        int type = (int)ownerType;
        IQueryable<DbItem> query = db.Items.AsNoTracking()
            .Where(i => i.OwnerType == type && i.OwnerId == ownerId);
        if (!string.IsNullOrEmpty(creatorAppId))
            query = query.Where(i => i.CreatorAppId == creatorAppId);
        if (!string.IsNullOrWhiteSpace(search)) {
            string term = search.Trim();
            query = query.Where(i => i.Name.Contains(term));
        }
        List<DbItem> rows = await query
            .OrderByDescending(i => i.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Item[]> GetItemsCreatedByApp(string appId, int limit, int offset = 0) {
        List<DbItem> rows = await db.Items.AsNoTracking()
            .Where(i => i.CreatorAppId == appId)
            .OrderByDescending(i => i.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }

    public async Task<Item[]> QueryItems(
        (BalanceOwnerType type, string id)? owner,
        string? creatorAppId,
        int limit, int offset) {

        IQueryable<DbItem> q = db.Items.AsNoTracking();

        if (owner is { } o) {
            int type = (int)o.type;
            q = q.Where(i => i.OwnerType == type && i.OwnerId == o.id);
        }
        if (!string.IsNullOrWhiteSpace(creatorAppId)) {
            q = q.Where(i => i.CreatorAppId == creatorAppId);
        }

        List<DbItem> rows = await q
            .OrderByDescending(i => i.DateCreated)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return rows.Select(Map).ToArray();
    }
}
