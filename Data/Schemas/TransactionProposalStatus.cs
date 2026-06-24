namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Lifecycle of an app-proposed, user-consented transaction.
/// Stored as an int in the <c>TransactionProposals</c> table.
/// </summary>
public enum TransactionProposalStatus {
    /// <summary>Awaiting the user's decision.</summary>
    Pending = 0,
    /// <summary>The user approved and the transfer is being executed (transient guard state).</summary>
    InProgress = 1,
    /// <summary>The user approved and the transfer succeeded.</summary>
    Approved = 2,
    /// <summary>The user explicitly declined.</summary>
    Denied = 3,
    /// <summary>The consent window elapsed before the user acted.</summary>
    Expired = 4,
    /// <summary>The proposing app withdrew the proposal before the user acted.</summary>
    Cancelled = 5,
    /// <summary>The user approved but the transfer could not be completed (e.g. insufficient funds).</summary>
    Failed = 6
}
