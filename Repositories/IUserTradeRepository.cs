using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>Which side of a trade the querying user is on.</summary>
public enum UserTradeRole {
    /// <summary>Trades where the user is either party.</summary>
    Any,
    /// <summary>Trades proposed to the user (they decide). The user is <c>ToUserId</c>.</summary>
    Incoming,
    /// <summary>Trades the user proposed. The user is <c>FromUserId</c>.</summary>
    Outgoing
}

public interface IUserTradeRepository {
    /// <summary>Persists a new trade in the <c>Pending</c> state.</summary>
    Task Create(UserTrade trade);

    /// <summary>
    /// Returns a trade by id, lazily flipping an overdue <c>Pending</c> trade to <c>Expired</c>
    /// first so callers never observe a stale pending window. Null if not found.
    /// </summary>
    Task<UserTrade?> GetById(string id);

    /// <summary>
    /// Lists a user's trades (newest first), optionally filtered by role and status. Overdue
    /// pending trades involving the user are lazily expired before the query.
    /// </summary>
    Task<UserTrade[]> ListForUser(string userId, UserTradeRole role, UserTradeStatus? status,
        int limit, int offset);

    /// <summary>
    /// Atomically transitions a trade from <c>Pending</c> to <c>InProgress</c>, guarded by id, the
    /// recipient, and the expiry window, so a concurrent approve can only win once. Returns the
    /// (now in-progress) trade on success, or null otherwise.
    /// </summary>
    Task<UserTrade?> TryBeginConsent(string id, string toUserId);

    /// <summary>Marks an in-progress trade <c>Approved</c>, recording the resulting transaction id.</summary>
    Task MarkApproved(string id, string? transactionId);

    /// <summary>Marks an in-progress trade <c>Failed</c> with a reason.</summary>
    Task MarkFailed(string id, string reason);

    /// <summary>Marks a pending trade <c>Denied</c> by the recipient. False if it was not pending.</summary>
    Task<bool> MarkDenied(string id, string toUserId);

    /// <summary>Marks a pending trade <c>Cancelled</c> by the initiator. False if it was not pending.</summary>
    Task<bool> MarkCancelled(string id, string fromUserId);
}
