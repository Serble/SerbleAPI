using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface ITransactionProposalRepository {
    /// <summary>Persists a new proposal in the <c>Pending</c> state.</summary>
    Task Create(TransactionProposal proposal);

    /// <summary>
    /// Returns a proposal by id, lazily flipping an overdue <c>Pending</c> proposal to
    /// <c>Expired</c> first so callers never observe a stale pending window. Null if not found.
    /// </summary>
    Task<TransactionProposal?> GetById(string id);

    /// <summary>
    /// Atomically transitions a proposal from <c>Pending</c> to <c>InProgress</c>, guarded by id,
    /// the consenting user, and the expiry window. Returns the (now in-progress) proposal on
    /// success, or null if it was not pending/owned-by-user/unexpired (so a concurrent approve
    /// can only win once). Used by the consent approve path before executing the transfer.
    /// </summary>
    Task<TransactionProposal?> TryBeginConsent(string id, string userId);

    /// <summary>Marks an in-progress proposal <c>Approved</c> and records the resulting transaction id.</summary>
    Task MarkApproved(string id, string transactionId);

    /// <summary>Marks an in-progress proposal <c>Failed</c> with a reason (transfer could not complete).</summary>
    Task MarkFailed(string id, string reason);

    /// <summary>Marks a pending proposal <c>Denied</c> by the user. Returns false if it was not pending.</summary>
    Task<bool> MarkDenied(string id);

    /// <summary>Marks a pending proposal <c>Cancelled</c> by the proposing app. Returns false if it was not pending.</summary>
    Task<bool> MarkCancelled(string id);
}
