using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// User-facing consent endpoints for app-proposed transactions, driven by the serble website
/// (mirroring the OAuth consent screen). The controller-level <c>[Authorize(Policy = "UserOnly")]</c>
/// ensures the caller is a logged-in user; only the proposal's payer may view or act on it.
///
/// Approving atomically claims the pending proposal and then executes the (zero-sum, audited)
/// transfer from the consenting user to the app-specified recipient.
/// </summary>
[ApiController]
[Route("api/v1/transactions/consent")]
[Authorize(Policy = "UserOnly")]
public class TransactionConsentController(
    ILogger<TransactionConsentController> logger,
    ITransactionProposalRepository proposalRepo,
    ITransactionRepository transactionRepo,
    IUserRepository userRepo,
    IAppRepository appRepo) : ControllerManager {

    public class ConsentInfoResponse {
        public string ProposalId { get; set; } = "";
        public string Status { get; set; } = "";
        public string AppId { get; set; } = "";
        public string AppName { get; set; } = "";
        public string AppDescription { get; set; } = "";
        public string RecipientType { get; set; } = "";
        public string RecipientId { get; set; } = "";
        public string RecipientName { get; set; } = "";
        public ulong Amount { get; set; }
        public string? Description { get; set; }
        /// <summary>Where the user will be returned to after deciding, if the app supplied one.</summary>
        public string? RedirectUri { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class ConsentResultResponse {
        public string ProposalId { get; set; } = "";
        public string Status { get; set; } = "";
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }
        /// <summary>Full URL to navigate the user to after the decision (the app's redirect with
        /// <c>proposal</c> and <c>status</c> query params appended), or null if none was supplied.</summary>
        public string? Redirect { get; set; }
    }

    /// <summary>Appends the proposal id and outcome to the app-supplied redirect, or null if none.</summary>
    private static string? BuildRedirect(string? redirectUri, string proposalId, TransactionProposalStatus status) {
        if (string.IsNullOrWhiteSpace(redirectUri)) return null;
        return QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?> {
            ["proposal"] = proposalId,
            ["status"]   = status.ToString()
        });
    }

    private async Task<string> ResolveRecipientName(BalanceOwnerType type, string id) {
        switch (type) {
            case BalanceOwnerType.App:
                OAuthApp? app = await appRepo.GetOAuthApp(id);
                return app?.Name ?? id;
            case BalanceOwnerType.User:
                User? user = await userRepo.GetUser(id);
                return user?.Username ?? id;
            default:
                return id;
        }
    }

    /// <summary>Consent-screen data for a proposal. Only the payer may view it.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConsentInfoResponse>> GetInfo(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        TransactionProposal? proposal = await proposalRepo.GetById(id);
        if (proposal == null || proposal.UserId != userId) return NotFound("Proposal not found.");

        OAuthApp? app = await appRepo.GetOAuthApp(proposal.AppId);
        string recipientName = await ResolveRecipientName(proposal.RecipientType, proposal.RecipientId);

        return Ok(new ConsentInfoResponse {
            ProposalId     = proposal.Id,
            Status         = proposal.Status.ToString(),
            AppId          = proposal.AppId,
            AppName        = app?.Name ?? proposal.AppId,
            AppDescription = app?.Description ?? "",
            RecipientType  = proposal.RecipientType.ToString(),
            RecipientId    = proposal.RecipientId,
            RecipientName  = recipientName,
            Amount         = proposal.Amount,
            Description    = proposal.Description,
            RedirectUri    = proposal.RedirectUri,
            ExpiresAt      = proposal.ExpiresAt
        });
    }

    /// <summary>Approves the proposal and executes the transfer from the user to the recipient.</summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult<ConsentResultResponse>> Approve(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        // Look up first to give precise errors before the atomic claim.
        TransactionProposal? existing = await proposalRepo.GetById(id);
        if (existing == null || existing.UserId != userId) return NotFound("Proposal not found.");
        if (existing.Status != TransactionProposalStatus.Pending)
            return BadRequest($"Proposal is not pending (status: {existing.Status}).");

        // Atomically claim the proposal so a concurrent approve can't double-spend.
        TransactionProposal? proposal = await proposalRepo.TryBeginConsent(id, userId);
        if (proposal == null) return BadRequest("Proposal could not be approved (it may have just been resolved or expired).");

        TransferOutcome outcome;
        try {
            outcome = await transactionRepo.Transfer(
                BalanceOwnerType.User, userId,
                proposal.RecipientType, proposal.RecipientId,
                proposal.Amount, proposal.Description);
        }
        catch (Exception ex) {
            // The claim already moved the proposal to InProgress; ensure it can't be stranded
            // there if the transfer throws (the transfer's own SaveChanges rolled back, so no
            // coins moved). Move it to a terminal Failed state so it's auditable and unblocked.
            await proposalRepo.MarkFailed(id, "Transfer could not be completed.");
            logger.LogError(ex, "Proposal {ProposalId} approved by user {UserId} but transfer threw", id, userId);
            return Ok(new ConsentResultResponse {
                ProposalId    = id,
                Status        = TransactionProposalStatus.Failed.ToString(),
                FailureReason = "Transfer could not be completed.",
                Redirect      = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Failed)
            });
        }

        if (!outcome.Success) {
            string reason = outcome.Error switch {
                TransferError.ZeroAmount        => "Amount must be greater than zero.",
                TransferError.SameOwner         => "Payer and recipient are the same.",
                TransferError.InsufficientFunds => "Insufficient funds.",
                TransferError.RecipientOverflow => "Recipient balance would overflow.",
                _                               => "Transfer failed."
            };
            await proposalRepo.MarkFailed(id, reason);
            logger.LogInformation("Proposal {ProposalId} approved by user {UserId} but transfer failed: {Reason}",
                id, userId, reason);
            return Ok(new ConsentResultResponse {
                ProposalId    = id,
                Status        = TransactionProposalStatus.Failed.ToString(),
                FailureReason = reason,
                Redirect      = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Failed)
            });
        }

        await proposalRepo.MarkApproved(id, outcome.Transaction!.Id);
        logger.LogInformation("Proposal {ProposalId} approved by user {UserId}; transaction {TxId}",
            id, userId, outcome.Transaction.Id);
        return Ok(new ConsentResultResponse {
            ProposalId    = id,
            Status        = TransactionProposalStatus.Approved.ToString(),
            TransactionId = outcome.Transaction.Id,
            Redirect      = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Approved)
        });
    }

    /// <summary>Declines the proposal.</summary>
    [HttpPost("{id}/deny")]
    public async Task<ActionResult<ConsentResultResponse>> Deny(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        TransactionProposal? proposal = await proposalRepo.GetById(id);
        if (proposal == null || proposal.UserId != userId) return NotFound("Proposal not found.");
        if (proposal.Status != TransactionProposalStatus.Pending)
            return BadRequest($"Proposal is not pending (status: {proposal.Status}).");

        if (!await proposalRepo.MarkDenied(id))
            return BadRequest("Proposal could not be denied (it may have just been resolved).");

        logger.LogInformation("Proposal {ProposalId} denied by user {UserId}", id, userId);
        return Ok(new ConsentResultResponse {
            ProposalId = id,
            Status     = TransactionProposalStatus.Denied.ToString(),
            Redirect   = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Denied)
        });
    }
}
