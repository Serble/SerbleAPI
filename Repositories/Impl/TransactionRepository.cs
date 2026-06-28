using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public async Task<TradeOutcome> ExecuteTrade(TransactionProposal proposal) {
        // Serialize the read-validate-move-save under a serializable transaction so two
        // proposals touching the same item/balance can't both pass re-validation concurrently
        // (TOCTOU). On a serialization failure the save throws and the caller marks the proposal
        // Failed. Non-relational providers (tests) skip the explicit transaction.
        await using IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;

        TradeOutcome outcome = await ExecuteTradeCore(proposal);
        if (outcome.Success && tx != null) await tx.CommitAsync();
        return outcome;
    }

    private async Task<TradeOutcome> ExecuteTradeCore(TransactionProposal proposal) {
        ulong requestedCoins = proposal.Amount;
        ulong offeredCoins = proposal.OfferedCoins;
        List<string> offeredItemIds = proposal.OfferedItemIds.Distinct().ToList();
        List<string> requestedItemIds = proposal.RequestedItemIds.Distinct().ToList();

        if (requestedCoins == 0 && offeredCoins == 0
            && offeredItemIds.Count == 0 && requestedItemIds.Count == 0)
            return TradeOutcome.Fail(TradeError.Empty);

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

        // Resolve the coin balances we'll touch (tracked, created on demand).
        DbTransaction? requestedTx = null;
        DbTransaction? offeredTx = null;

        if (requestedCoins > 0) {
            DbBalance payer = await GetOrCreateDefault(BalanceOwnerType.User, proposal.UserId);
            DbBalance recipient = await GetOrCreateDefault(proposal.RecipientType, proposal.RecipientId);
            if (payer.Coins < requestedCoins) return TradeOutcome.Fail(TradeError.InsufficientUserFunds);
            if (recipient.Coins > ulong.MaxValue - requestedCoins) return TradeOutcome.Fail(TradeError.Overflow);
            payer.Coins -= requestedCoins;
            recipient.Coins += requestedCoins;
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
            DbBalance appBalance = await GetOrCreateDefault(BalanceOwnerType.App, proposal.AppId);
            DbBalance userBalance = await GetOrCreateDefault(BalanceOwnerType.User, proposal.UserId);
            if (appBalance.Coins < offeredCoins) return TradeOutcome.Fail(TradeError.InsufficientAppFunds);
            if (userBalance.Coins > ulong.MaxValue - offeredCoins) return TradeOutcome.Fail(TradeError.Overflow);
            appBalance.Coins -= offeredCoins;
            userBalance.Coins += offeredCoins;
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

    public async Task<TradeOutcome> ExecuteUserTrade(UserTrade trade) {
        // Same TOCTOU protection as ExecuteTrade: serialize read-validate-move-save so two trades
        // touching the same item/balance can't both pass re-validation concurrently.
        await using IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;

        TradeOutcome outcome = await ExecuteUserTradeCore(trade);
        if (outcome.Success && tx != null) await tx.CommitAsync();
        return outcome;
    }

    private async Task<TradeOutcome> ExecuteUserTradeCore(UserTrade trade) {
        ulong offeredCoins = trade.OfferedCoins;       // initiator (from) → recipient (to)
        ulong requestedCoins = trade.RequestedCoins;   // recipient (to) → initiator (from)
        List<string> offeredItemIds = trade.OfferedItemIds.Distinct().ToList();
        List<string> requestedItemIds = trade.RequestedItemIds.Distinct().ToList();

        if (offeredCoins == 0 && requestedCoins == 0
            && offeredItemIds.Count == 0 && requestedItemIds.Count == 0)
            return TradeOutcome.Fail(TradeError.Empty);

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
            DbBalance from = await GetOrCreateDefault(BalanceOwnerType.User, trade.FromUserId);
            DbBalance to = await GetOrCreateDefault(BalanceOwnerType.User, trade.ToUserId);
            if (from.Coins < offeredCoins) return TradeOutcome.Fail(TradeError.InsufficientInitiatorFunds);
            if (to.Coins > ulong.MaxValue - offeredCoins) return TradeOutcome.Fail(TradeError.Overflow);
            from.Coins -= offeredCoins;
            to.Coins += offeredCoins;
            offeredTx = new DbTransaction {
                Id = Guid.NewGuid().ToString(), FromBalanceId = from.Id, ToBalanceId = to.Id,
                Amount = offeredCoins, Description = trade.Description, DateCreated = DateTime.UtcNow
            };
            db.Transactions.Add(offeredTx);
        }

        if (requestedCoins > 0) {
            DbBalance from = await GetOrCreateDefault(BalanceOwnerType.User, trade.ToUserId);
            DbBalance to = await GetOrCreateDefault(BalanceOwnerType.User, trade.FromUserId);
            if (from.Coins < requestedCoins) return TradeOutcome.Fail(TradeError.InsufficientCounterpartyFunds);
            if (to.Coins > ulong.MaxValue - requestedCoins) return TradeOutcome.Fail(TradeError.Overflow);
            from.Coins -= requestedCoins;
            to.Coins += requestedCoins;
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
