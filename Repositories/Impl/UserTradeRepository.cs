using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class UserTradeRepository(SerbleDbContext db) : IUserTradeRepository {

    private static UserTrade Map(DbUserTrade r, IEnumerable<DbUserTradeItem> items) {
        List<DbUserTradeItem> list = items.ToList();
        return new UserTrade {
            Id               = r.Id,
            FromUserId       = r.FromUserId,
            ToUserId         = r.ToUserId,
            OfferedCoins     = r.OfferedCoins,
            RequestedCoins   = r.RequestedCoins,
            OfferedItemIds   = list.Where(i => i.Direction == (int)ProposalItemDirection.Offer)
                                   .Select(i => i.ItemId).ToList(),
            RequestedItemIds = list.Where(i => i.Direction == (int)ProposalItemDirection.Request)
                                   .Select(i => i.ItemId).ToList(),
            Description      = r.Description,
            Status           = (UserTradeStatus)r.Status,
            TransactionId    = r.TransactionId,
            FailureReason    = r.FailureReason,
            CreatedAt        = r.CreatedAt,
            ExpiresAt        = r.ExpiresAt,
            ResolvedAt       = r.ResolvedAt
        };
    }

    private async Task<UserTrade?> LoadById(string id) {
        DbUserTrade? row = await db.UserTrades.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (row == null) return null;
        List<DbUserTradeItem> items = await db.UserTradeItems.AsNoTracking()
            .Where(i => i.TradeId == id).ToListAsync();
        return Map(row, items);
    }

    public async Task Create(UserTrade trade) {
        db.UserTrades.Add(new DbUserTrade {
            Id             = trade.Id,
            FromUserId     = trade.FromUserId,
            ToUserId       = trade.ToUserId,
            OfferedCoins   = trade.OfferedCoins,
            RequestedCoins = trade.RequestedCoins,
            Description    = trade.Description,
            Status         = (int)UserTradeStatus.Pending,
            CreatedAt      = trade.CreatedAt,
            ExpiresAt      = trade.ExpiresAt
        });
        foreach (string itemId in trade.OfferedItemIds) {
            db.UserTradeItems.Add(new DbUserTradeItem {
                Id = Guid.NewGuid().ToString(), TradeId = trade.Id, ItemId = itemId,
                Direction = (int)ProposalItemDirection.Offer
            });
        }
        foreach (string itemId in trade.RequestedItemIds) {
            db.UserTradeItems.Add(new DbUserTradeItem {
                Id = Guid.NewGuid().ToString(), TradeId = trade.Id, ItemId = itemId,
                Direction = (int)ProposalItemDirection.Request
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<UserTrade?> GetById(string id) {
        DateTime now = DateTime.UtcNow;
        await db.UserTrades
            .Where(t => t.Id == id && t.Status == (int)UserTradeStatus.Pending && t.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (int)UserTradeStatus.Expired)
                .SetProperty(t => t.ResolvedAt, now));
        return await LoadById(id);
    }

    public async Task<UserTrade[]> ListForUser(string userId, UserTradeRole role,
        UserTradeStatus? status, int limit, int offset) {
        // Lazily expire the user's overdue pending trades so the list never shows a stale window.
        DateTime now = DateTime.UtcNow;
        await db.UserTrades
            .Where(t => (t.FromUserId == userId || t.ToUserId == userId)
                     && t.Status == (int)UserTradeStatus.Pending && t.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (int)UserTradeStatus.Expired)
                .SetProperty(t => t.ResolvedAt, now));

        IQueryable<DbUserTrade> q = role switch {
            UserTradeRole.Incoming => db.UserTrades.Where(t => t.ToUserId == userId),
            UserTradeRole.Outgoing => db.UserTrades.Where(t => t.FromUserId == userId),
            _                      => db.UserTrades.Where(t => t.FromUserId == userId || t.ToUserId == userId)
        };
        if (status.HasValue) {
            int s = (int)status.Value;
            q = q.Where(t => t.Status == s);
        }

        List<DbUserTrade> rows = await q.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Skip(offset).Take(limit).ToListAsync();

        if (rows.Count == 0) return [];

        // Fetch all item legs for the page in one query, then group by trade.
        List<string> ids = rows.Select(r => r.Id).ToList();
        List<DbUserTradeItem> allItems = await db.UserTradeItems.AsNoTracking()
            .Where(i => ids.Contains(i.TradeId)).ToListAsync();
        ILookup<string, DbUserTradeItem> byTrade = allItems.ToLookup(i => i.TradeId);
        return rows.Select(r => Map(r, byTrade[r.Id])).ToArray();
    }

    public async Task<UserTrade?> TryBeginConsent(string id, string toUserId) {
        DateTime now = DateTime.UtcNow;
        int affected = await db.UserTrades
            .Where(t => t.Id == id && t.ToUserId == toUserId
                     && t.Status == (int)UserTradeStatus.Pending && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, (int)UserTradeStatus.InProgress));
        if (affected == 0) return null;
        return await LoadById(id);
    }

    public Task MarkApproved(string id, string? transactionId) {
        DateTime now = DateTime.UtcNow;
        return db.UserTrades
            .Where(t => t.Id == id && t.Status == (int)UserTradeStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (int)UserTradeStatus.Approved)
                .SetProperty(t => t.TransactionId, transactionId)
                .SetProperty(t => t.ResolvedAt, now));
    }

    public Task MarkFailed(string id, string reason) {
        DateTime now = DateTime.UtcNow;
        return db.UserTrades
            .Where(t => t.Id == id && t.Status == (int)UserTradeStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (int)UserTradeStatus.Failed)
                .SetProperty(t => t.FailureReason, reason)
                .SetProperty(t => t.ResolvedAt, now));
    }

    public Task<bool> MarkDenied(string id, string toUserId) =>
        TransitionFromPending(id, t => t.ToUserId == toUserId, UserTradeStatus.Denied);

    public Task<bool> MarkCancelled(string id, string fromUserId) =>
        TransitionFromPending(id, t => t.FromUserId == fromUserId, UserTradeStatus.Cancelled);

    private async Task<bool> TransitionFromPending(string id,
        System.Linq.Expressions.Expression<Func<DbUserTrade, bool>> actorGuard, UserTradeStatus to) {
        DateTime now = DateTime.UtcNow;
        int affected = await db.UserTrades
            .Where(t => t.Id == id
                     && t.Status == (int)UserTradeStatus.Pending && t.ExpiresAt > now)
            .Where(actorGuard)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (int)to)
                .SetProperty(t => t.ResolvedAt, now));
        return affected > 0;
    }
}
