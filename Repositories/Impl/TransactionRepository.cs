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

    public Task<TransferOutcome> Transfer(
        BalanceOwnerType fromType, string fromId,
        BalanceOwnerType toType, string toId,
        ulong amount, string? description) {

        // Cheap rejections that need no database state, so they never open a transaction.
        if (amount == 0) return Task.FromResult(TransferOutcome.Fail(TransferError.ZeroAmount));
        if (fromType == toType && fromId == toId)
            return Task.FromResult(TransferOutcome.Fail(TransferError.SameOwner));

        return BalanceLocking.Run(db,
            () => TransferCore(fromType, fromId, toType, toId, amount, description),
            outcome => outcome.Success);
    }

    private async Task<TransferOutcome> TransferCore(
        BalanceOwnerType fromType, string fromId,
        BalanceOwnerType toType, string toId,
        ulong amount, string? description) {

        // Both rows are taken FOR UPDATE before either is looked at, so the funds check below
        // still holds when the write lands: a concurrent transfer out of the same balance waits
        // here rather than reading the same starting figure and crediting a second recipient.
        Dictionary<(BalanceOwnerType type, string id), DbBalance> balances =
            await BalanceLocking.LockDefaults(db, [(fromType, fromId), (toType, toId)]);
        DbBalance from = balances[(fromType, fromId)];
        DbBalance to = balances[(toType, toId)];

        if (from.Coins < amount) return TransferOutcome.Fail(TransferError.InsufficientFunds);
        if (!to.CanCredit(amount)) return TransferOutcome.Fail(TransferError.RecipientOverflow);

        // Zero-sum: deduct from sender, add to receiver, record the movement — one atomic save.
        from.SetCoins(from.Coins - amount);
        to.SetCoins(to.Coins + amount);

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

    public Task<TradeOutcome> ExecuteTrade(TransactionProposal proposal) =>
        // Same discipline as a plain transfer, plus the item legs: every balance the trade touches
        // is locked before anything is validated, and the serializable transaction covers the item
        // rows so two proposals cannot both pass re-validation against the same item (TOCTOU).
        // A trade rolled back for a deadlock is retried rather than reported as failed.
        BalanceLocking.Run(db, () => ExecuteTradeCore(proposal), outcome => outcome.Success);

    private async Task<TradeOutcome> ExecuteTradeCore(TransactionProposal proposal) {
        ulong requestedCoins = proposal.Amount;
        ulong offeredCoins = proposal.OfferedCoins;
        List<string> offeredItemIds = proposal.OfferedItemIds.Distinct().ToList();
        List<string> requestedItemIds = proposal.RequestedItemIds.Distinct().ToList();

        if (requestedCoins == 0 && offeredCoins == 0
            && offeredItemIds.Count == 0 && requestedItemIds.Count == 0)
            return TradeOutcome.Fail(TradeError.Empty);

        // Locks first, in one ordered batch, before any balance is read or checked. Which balances
        // the trade needs is known from the proposal alone, so this does not depend on anything
        // read below.
        List<(BalanceOwnerType, string)> owners = [];
        if (requestedCoins > 0) {
            owners.Add((BalanceOwnerType.User, proposal.UserId));
            owners.Add((proposal.RecipientType, proposal.RecipientId));
        }
        if (offeredCoins > 0) {
            owners.Add((BalanceOwnerType.App, proposal.AppId));
            owners.Add((BalanceOwnerType.User, proposal.UserId));
        }
        Dictionary<(BalanceOwnerType type, string id), DbBalance> balances =
            await BalanceLocking.LockDefaults(db, owners);

        // Load every referenced item (tracked, so ownership edits are saved with the trade).
        List<string> allItemIds = offeredItemIds.Concat(requestedItemIds).ToList();
        Dictionary<string, DbItem> items = allItemIds.Count == 0
            ? new Dictionary<string, DbItem>()
            : await db.Items.Where(i => allItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        // Validate item existence and ownership before moving anything.
        foreach (string itemId in offeredItemIds) {
            if (!items.TryGetValue(itemId, out DbItem? item)) return TradeOutcome.Fail(TradeError.ItemNotFound);
            if (item.OwnerType != (int)BalanceOwnerType.App || item.OwnerId != proposal.AppId)
                return TradeOutcome.Fail(TradeError.OfferedItemNotOwned);
        }
        foreach (string itemId in requestedItemIds) {
            if (!items.TryGetValue(itemId, out DbItem? item)) return TradeOutcome.Fail(TradeError.ItemNotFound);
            if (item.OwnerType != (int)BalanceOwnerType.User || item.OwnerId != proposal.UserId)
                return TradeOutcome.Fail(TradeError.RequestedItemNotOwned);
        }

        DbTransaction? requestedTx = null;
        DbTransaction? offeredTx = null;

        if (requestedCoins > 0) {
            DbBalance payer = balances[(BalanceOwnerType.User, proposal.UserId)];
            DbBalance recipient = balances[(proposal.RecipientType, proposal.RecipientId)];
            if (payer.Coins < requestedCoins) return TradeOutcome.Fail(TradeError.InsufficientUserFunds);
            if (!recipient.CanCredit(requestedCoins)) return TradeOutcome.Fail(TradeError.Overflow);
            payer.SetCoins(payer.Coins - requestedCoins);
            recipient.SetCoins(recipient.Coins + requestedCoins);
            requestedTx = new DbTransaction {
                Id            = Guid.NewGuid().ToString(),
                FromBalanceId = payer.Id,
                ToBalanceId   = recipient.Id,
                Amount        = requestedCoins,
                Description   = proposal.Description,
                DateCreated   = DateTime.UtcNow
            };
            db.Transactions.Add(requestedTx);
        }

        if (offeredCoins > 0) {
            DbBalance appBalance = balances[(BalanceOwnerType.App, proposal.AppId)];
            DbBalance userBalance = balances[(BalanceOwnerType.User, proposal.UserId)];
            if (appBalance.Coins < offeredCoins) return TradeOutcome.Fail(TradeError.InsufficientAppFunds);
            if (!userBalance.CanCredit(offeredCoins)) return TradeOutcome.Fail(TradeError.Overflow);
            appBalance.SetCoins(appBalance.Coins - offeredCoins);
            userBalance.SetCoins(userBalance.Coins + offeredCoins);
            offeredTx = new DbTransaction {
                Id            = Guid.NewGuid().ToString(),
                FromBalanceId = appBalance.Id,
                ToBalanceId   = userBalance.Id,
                Amount        = offeredCoins,
                Description   = proposal.Description,
                DateCreated   = DateTime.UtcNow
            };
            db.Transactions.Add(offeredTx);
        }

        // Reassign item ownership, recording each move in the item's ownership history. The
        // history rows are part of this same atomic save, so an item never moves without an
        // audit entry.
        DateTime movedAt = DateTime.UtcNow;
        foreach (string itemId in offeredItemIds) {
            DbItem item = items[itemId];
            BalanceOwnerType fromType = (BalanceOwnerType)item.OwnerType;
            string fromId = item.OwnerId;
            item.OwnerType = (int)BalanceOwnerType.User;
            item.OwnerId = proposal.UserId;
            db.ItemTransactions.Add(DbItemTransaction.Trade(
                itemId, fromType, fromId, BalanceOwnerType.User, proposal.UserId, proposal.Id, movedAt));
        }
        foreach (string itemId in requestedItemIds) {
            DbItem item = items[itemId];
            BalanceOwnerType fromType = (BalanceOwnerType)item.OwnerType;
            string fromId = item.OwnerId;
            item.OwnerType = (int)BalanceOwnerType.App;
            item.OwnerId = proposal.AppId;
            db.ItemTransactions.Add(DbItemTransaction.Trade(
                itemId, fromType, fromId, BalanceOwnerType.App, proposal.AppId, proposal.Id, movedAt));
        }

        await db.SaveChangesAsync();
        return new TradeOutcome {
            Error                = TradeError.None,
            RequestedTransaction = requestedTx == null ? null : Map(requestedTx),
            OfferedTransaction   = offeredTx == null ? null : Map(offeredTx)
        };
    }

    public Task<TradeOutcome> ExecuteUserTrade(UserTrade trade) =>
        // Same protection as ExecuteTrade: balances locked in a fixed order before validation, the
        // whole read-validate-move-save serialized, and a rolled-back attempt retried.
        BalanceLocking.Run(db, () => ExecuteUserTradeCore(trade), outcome => outcome.Success);

    private async Task<TradeOutcome> ExecuteUserTradeCore(UserTrade trade) {
        ulong offeredCoins = trade.OfferedCoins;       // initiator (from) → recipient (to)
        ulong requestedCoins = trade.RequestedCoins;   // recipient (to) → initiator (from)
        List<string> offeredItemIds = trade.OfferedItemIds.Distinct().ToList();
        List<string> requestedItemIds = trade.RequestedItemIds.Distinct().ToList();

        if (offeredCoins == 0 && requestedCoins == 0
            && offeredItemIds.Count == 0 && requestedItemIds.Count == 0)
            return TradeOutcome.Fail(TradeError.Empty);

        // Both parties' balances, locked in a fixed order before anything is validated. A trade in
        // each direction between the same pair therefore queues instead of deadlocking.
        List<(BalanceOwnerType, string)> owners = [];
        if (offeredCoins > 0 || requestedCoins > 0) {
            owners.Add((BalanceOwnerType.User, trade.FromUserId));
            owners.Add((BalanceOwnerType.User, trade.ToUserId));
        }
        Dictionary<(BalanceOwnerType type, string id), DbBalance> balances =
            await BalanceLocking.LockDefaults(db, owners);

        // Load every referenced item (tracked, so ownership edits are saved with the trade).
        List<string> allItemIds = offeredItemIds.Concat(requestedItemIds).ToList();
        Dictionary<string, DbItem> items = allItemIds.Count == 0
            ? new Dictionary<string, DbItem>()
            : await db.Items.Where(i => allItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        // Validate item existence and ownership before moving anything. Offered items must be owned
        // by the initiator; requested items by the recipient.
        foreach (string itemId in offeredItemIds) {
            if (!items.TryGetValue(itemId, out DbItem? item)) return TradeOutcome.Fail(TradeError.ItemNotFound);
            if (item.OwnerType != (int)BalanceOwnerType.User || item.OwnerId != trade.FromUserId)
                return TradeOutcome.Fail(TradeError.OfferedItemNotOwned);
        }
        foreach (string itemId in requestedItemIds) {
            if (!items.TryGetValue(itemId, out DbItem? item)) return TradeOutcome.Fail(TradeError.ItemNotFound);
            if (item.OwnerType != (int)BalanceOwnerType.User || item.OwnerId != trade.ToUserId)
                return TradeOutcome.Fail(TradeError.RequestedItemNotOwned);
        }

        DbTransaction? offeredTx = null;
        DbTransaction? requestedTx = null;

        if (offeredCoins > 0) {
            DbBalance from = balances[(BalanceOwnerType.User, trade.FromUserId)];
            DbBalance to = balances[(BalanceOwnerType.User, trade.ToUserId)];
            if (from.Coins < offeredCoins) return TradeOutcome.Fail(TradeError.InsufficientInitiatorFunds);
            if (!to.CanCredit(offeredCoins)) return TradeOutcome.Fail(TradeError.Overflow);
            from.SetCoins(from.Coins - offeredCoins);
            to.SetCoins(to.Coins + offeredCoins);
            offeredTx = new DbTransaction {
                Id = Guid.NewGuid().ToString(), FromBalanceId = from.Id, ToBalanceId = to.Id,
                Amount = offeredCoins, Description = trade.Description, DateCreated = DateTime.UtcNow
            };
            db.Transactions.Add(offeredTx);
        }

        if (requestedCoins > 0) {
            DbBalance from = balances[(BalanceOwnerType.User, trade.ToUserId)];
            DbBalance to = balances[(BalanceOwnerType.User, trade.FromUserId)];
            if (from.Coins < requestedCoins) return TradeOutcome.Fail(TradeError.InsufficientCounterpartyFunds);
            if (!to.CanCredit(requestedCoins)) return TradeOutcome.Fail(TradeError.Overflow);
            from.SetCoins(from.Coins - requestedCoins);
            to.SetCoins(to.Coins + requestedCoins);
            requestedTx = new DbTransaction {
                Id = Guid.NewGuid().ToString(), FromBalanceId = from.Id, ToBalanceId = to.Id,
                Amount = requestedCoins, Description = trade.Description, DateCreated = DateTime.UtcNow
            };
            db.Transactions.Add(requestedTx);
        }

        // Reassign item ownership, recording each move in the item's ownership history (same save).
        DateTime movedAt = DateTime.UtcNow;
        foreach (string itemId in offeredItemIds) {
            DbItem item = items[itemId];
            BalanceOwnerType fromType = (BalanceOwnerType)item.OwnerType;
            string fromId = item.OwnerId;
            item.OwnerType = (int)BalanceOwnerType.User;
            item.OwnerId = trade.ToUserId;
            db.ItemTransactions.Add(DbItemTransaction.Trade(
                itemId, fromType, fromId, BalanceOwnerType.User, trade.ToUserId, trade.Id, movedAt));
        }
        foreach (string itemId in requestedItemIds) {
            DbItem item = items[itemId];
            BalanceOwnerType fromType = (BalanceOwnerType)item.OwnerType;
            string fromId = item.OwnerId;
            item.OwnerType = (int)BalanceOwnerType.User;
            item.OwnerId = trade.FromUserId;
            db.ItemTransactions.Add(DbItemTransaction.Trade(
                itemId, fromType, fromId, BalanceOwnerType.User, trade.FromUserId, trade.Id, movedAt));
        }

        await db.SaveChangesAsync();
        return new TradeOutcome {
            Error                = TradeError.None,
            OfferedTransaction   = offeredTx == null ? null : Map(offeredTx),
            RequestedTransaction = requestedTx == null ? null : Map(requestedTx)
        };
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

}
