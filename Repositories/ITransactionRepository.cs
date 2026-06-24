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
