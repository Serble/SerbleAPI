using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>Why a transfer failed (or <see cref="None"/> on success).</summary>
public enum TransferError {
    None = 0,
    ZeroAmount,
    SameOwner,
    InsufficientFunds,
    RecipientOverflow
}

/// <summary>Why a trade failed (or <see cref="None"/> on success).</summary>
public enum TradeError {
    None = 0,
    /// <summary>The trade moved no coins and no items.</summary>
    Empty,
    /// <summary>An item referenced by the trade no longer exists.</summary>
    ItemNotFound,
    /// <summary>The app no longer owns an item it offered.</summary>
    OfferedItemNotOwned,
    /// <summary>The user no longer owns an item the app requested.</summary>
    RequestedItemNotOwned,
    /// <summary>The user (payer) has insufficient coins for the requested amount.</summary>
    InsufficientUserFunds,
    /// <summary>The app has insufficient coins for the offered amount.</summary>
    InsufficientAppFunds,
    /// <summary>A recipient coin balance would overflow.</summary>
    Overflow,
    /// <summary>In a user↔user trade, the initiator has insufficient coins for the offered amount.</summary>
    InsufficientInitiatorFunds,
    /// <summary>In a user↔user trade, the recipient has insufficient coins for the requested amount.</summary>
    InsufficientCounterpartyFunds
}

/// <summary>Result of an attempted atomic trade (coins + items).</summary>
public class TradeOutcome {
    public bool Success => Error == TradeError.None;
    public TradeError Error { get; init; }
    /// <summary>The audit record for the coins the user paid the recipient, if any.</summary>
    public Transaction? RequestedTransaction { get; init; }
    /// <summary>The audit record for the coins the app paid the user, if any.</summary>
    public Transaction? OfferedTransaction { get; init; }

    public static TradeOutcome Fail(TradeError error) => new() { Error = error };
}

/// <summary>Result of an attempted coin transfer.</summary>
public class TransferOutcome {
    public bool Success => Error == TransferError.None;
    public TransferError Error { get; init; }
    public Transaction? Transaction { get; init; }
    public Balance? FromBalance { get; init; }
    public Balance? ToBalance { get; init; }

    public static TransferOutcome Fail(TransferError error) => new() { Error = error };

    public static TransferOutcome Ok(Transaction tx, Balance from, Balance to) => new() {
        Error = TransferError.None,
        Transaction = tx,
        FromBalance = from,
        ToBalance = to
    };
}

public interface ITransactionRepository {
    /// <summary>
    /// Atomically moves <paramref name="amount"/> coins from one owner's default balance to
    /// another's, writing an audit record and updating both balances in a single save. The
    /// operation is zero-sum: it fails (without mutating anything) on zero amount, a transfer
    /// to the same owner, insufficient sender funds, or recipient overflow.
    /// </summary>
    Task<TransferOutcome> Transfer(
        BalanceOwnerType fromType, string fromId,
        BalanceOwnerType toType, string toId,
        ulong amount, string? description);

    /// <summary>
    /// Atomically executes a trade described by an approved <paramref name="proposal"/> in a single
    /// save: the user pays the recipient the requested coins, the app pays the user the offered
    /// coins, offered items move app → user, and requested items move user → app. The operation is
    /// all-or-nothing — it fails (mutating nothing) if any item is no longer owned by the expected
    /// party, either party lacks funds, or a balance would overflow. Coin legs with a zero amount
    /// are skipped.
    /// </summary>
    Task<TradeOutcome> ExecuteTrade(TransactionProposal proposal);

    /// <summary>
    /// Atomically executes a user↔user trade described by an approved <paramref name="trade"/> in a
    /// single save: the initiator pays the recipient the offered coins, the recipient pays the
    /// initiator the requested coins, offered items move initiator → recipient, and requested items
    /// move recipient → initiator. All-or-nothing — it fails (mutating nothing) if any item is no
    /// longer owned by the expected party, either party lacks funds, or a balance would overflow.
    /// Zero-amount coin legs are skipped. On success, <c>OfferedTransaction</c> is the initiator →
    /// recipient leg and <c>RequestedTransaction</c> the recipient → initiator leg.
    /// </summary>
    Task<TradeOutcome> ExecuteUserTrade(UserTrade trade);

    /// <summary>Returns transactions touching a specific balance (newest first), paginated.</summary>
    Task<Transaction[]> GetTransactionsForBalance(string balanceId, int limit, int offset = 0);

    /// <summary>Returns transactions touching any of an owner's balances (newest first), paginated.</summary>
    Task<Transaction[]> GetTransactionsForOwner(BalanceOwnerType ownerType, string ownerId, int limit, int offset = 0);

    /// <summary>
    /// Admin audit query. Each optional filter is matched against the resolved balances of an
    /// owner: <paramref name="any"/> matches transactions where the owner is sender OR recipient;
    /// <paramref name="from"/> matches sender; <paramref name="to"/> matches recipient. Results
    /// are newest first and paginated.
    /// </summary>
    Task<Transaction[]> QueryTransactions(
        (BalanceOwnerType type, string id)? any,
        (BalanceOwnerType type, string id)? from,
        (BalanceOwnerType type, string id)? to,
        int limit, int offset);
}
