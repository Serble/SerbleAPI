using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class TransactionProposalRepository(SerbleDbContext db) : ITransactionProposalRepository {

    private static TransactionProposal Map(DbTransactionProposal r) => new() {
        Id            = r.Id,
        AppId         = r.AppId,
        UserId        = r.UserId,
        RecipientType = (BalanceOwnerType)r.RecipientType,
        RecipientId   = r.RecipientId,
        Amount        = r.Amount,
        Description   = r.Description,
        RedirectUri   = r.RedirectUri,
        Status        = (TransactionProposalStatus)r.Status,
        TransactionId = r.TransactionId,
        FailureReason = r.FailureReason,
        CreatedAt     = r.CreatedAt,
        ExpiresAt     = r.ExpiresAt,
        ResolvedAt    = r.ResolvedAt
    };

    public Task Create(TransactionProposal proposal) {
        db.TransactionProposals.Add(new DbTransactionProposal {
            Id            = proposal.Id,
            AppId         = proposal.AppId,
            UserId        = proposal.UserId,
            RecipientType = (int)proposal.RecipientType,
            RecipientId   = proposal.RecipientId,
            Amount        = proposal.Amount,
            Description   = proposal.Description,
            RedirectUri   = proposal.RedirectUri,
            Status        = (int)TransactionProposalStatus.Pending,
            CreatedAt     = proposal.CreatedAt,
            ExpiresAt     = proposal.ExpiresAt
        });
        return db.SaveChangesAsync();
    }

    public async Task<TransactionProposal?> GetById(string id) {
        // Lazily expire an overdue pending proposal so callers never see a stale window.
        DateTime now = DateTime.UtcNow;
        await db.TransactionProposals
            .Where(p => p.Id == id
                     && p.Status == (int)TransactionProposalStatus.Pending
                     && p.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, (int)TransactionProposalStatus.Expired)
                .SetProperty(p => p.ResolvedAt, now));

        DbTransactionProposal? row = await db.TransactionProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        return row == null ? null : Map(row);
    }

    public async Task<TransactionProposal?> TryBeginConsent(string id, string userId) {
        DateTime now = DateTime.UtcNow;
        // Single atomic UPDATE so a concurrent approve of the same proposal can only win once.
        int affected = await db.TransactionProposals
            .Where(p => p.Id == id
                     && p.UserId == userId
                     && p.Status == (int)TransactionProposalStatus.Pending
                     && p.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, (int)TransactionProposalStatus.InProgress));
        if (affected == 0) return null;

        DbTransactionProposal? row = await db.TransactionProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
        return row == null ? null : Map(row);
    }

    public Task MarkApproved(string id, string transactionId) {
        DateTime now = DateTime.UtcNow;
        return db.TransactionProposals
            .Where(p => p.Id == id && p.Status == (int)TransactionProposalStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, (int)TransactionProposalStatus.Approved)
                .SetProperty(p => p.TransactionId, transactionId)
                .SetProperty(p => p.ResolvedAt, now));
    }

    public Task MarkFailed(string id, string reason) {
        DateTime now = DateTime.UtcNow;
        return db.TransactionProposals
            .Where(p => p.Id == id && p.Status == (int)TransactionProposalStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, (int)TransactionProposalStatus.Failed)
                .SetProperty(p => p.FailureReason, reason)
                .SetProperty(p => p.ResolvedAt, now));
    }

    public async Task<bool> MarkDenied(string id) =>
        await TransitionFromPending(id, TransactionProposalStatus.Denied);

    public async Task<bool> MarkCancelled(string id) =>
        await TransitionFromPending(id, TransactionProposalStatus.Cancelled);

    private async Task<bool> TransitionFromPending(string id, TransactionProposalStatus to) {
        DateTime now = DateTime.UtcNow;
        int affected = await db.TransactionProposals
            .Where(p => p.Id == id
                     && p.Status == (int)TransactionProposalStatus.Pending
                     && p.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, (int)to)
                .SetProperty(p => p.ResolvedAt, now));
        return affected > 0;
    }
}
