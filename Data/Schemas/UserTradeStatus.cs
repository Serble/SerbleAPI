namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Lifecycle of a user-to-user trade. Stored as an int in the <c>UserTrades</c> table.
/// </summary>
public enum UserTradeStatus {
    /// <summary>Awaiting the recipient's decision.</summary>
    Pending = 0,
    /// <summary>The recipient approved and the swap is executing (transient guard state).</summary>
    InProgress = 1,
    /// <summary>The recipient approved and the swap succeeded (complete).</summary>
    Approved = 2,
    /// <summary>The recipient declined (rejected).</summary>
    Denied = 3,
    /// <summary>The window elapsed before the recipient acted.</summary>
    Expired = 4,
    /// <summary>The proposing user withdrew the trade before the recipient acted.</summary>
    Cancelled = 5,
    /// <summary>The recipient approved but the swap could not complete (e.g. insufficient funds).</summary>
    Failed = 6
}
