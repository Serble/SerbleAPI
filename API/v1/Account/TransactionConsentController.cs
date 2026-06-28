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
    IItemRepository itemRepo,
    IAppRepository appRepo) : ControllerManager {

    public class ItemView {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? IconUrl { get; set; }
        public string? Description { get; set; }
    }

    public class ConsentInfoResponse {
        public string ProposalId { get; set; } = "";
        public string Status { get; set; } = "";
        public string AppId { get; set; } = "";
        public string AppName { get; set; } = "";
        public string AppDescription { get; set; } = "";
        public string RecipientType { get; set; } = "";
        public string RecipientId { get; set; } = "";
        public string RecipientName { get; set; } = "";
        /// <summary>Coins the user pays the recipient (user → recipient).</summary>
        public ulong Amount { get; set; }
        /// <summary>Coins the app gives the user (app → user).</summary>
        public ulong OfferedCoins { get; set; }
        /// <summary>Items the app gives the user (app → user).</summary>
        public ItemView[] OfferedItems { get; set; } = [];
        /// <summary>Items the app requests from the user (user → app).</summary>
        public ItemView[] RequestedItems { get; set; } = [];
        /// <summary>True when the trade involves any items on either side.</summary>
        public bool InvolvesItems { get; set; }
        /// <summary>True when the app gives something and asks for nothing in return.</summary>
        public bool IsGift { get; set; }
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

    private async Task<ItemView[]> ResolveItems(IEnumerable<string> itemIds) {
        List<ItemView> views = new();
        foreach (string id in itemIds) {
            Item? item = await itemRepo.GetItem(id);
            views.Add(new ItemView {
                Id          = id,
                Name        = item?.Name ?? id,
                IconUrl     = item?.IconUrl,
                Description = item?.Description
            });
        }
        return views.ToArray();
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
        ItemView[] offeredItems = await ResolveItems(proposal.OfferedItemIds);
        ItemView[] requestedItems = await ResolveItems(proposal.RequestedItemIds);

        bool involvesItems = offeredItems.Length > 0 || requestedItems.Length > 0;
        bool isGift = proposal.Amount == 0 && requestedItems.Length == 0
                      && (proposal.OfferedCoins > 0 || offeredItems.Length > 0);

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
            OfferedCoins   = proposal.OfferedCoins,
            OfferedItems   = offeredItems,
            RequestedItems = requestedItems,
            InvolvesItems  = involvesItems,
            IsGift         = isGift,
            Description    = proposal.Description,
            RedirectUri    = proposal.RedirectUri,
            ExpiresAt      = proposal.ExpiresAt
        });
    }

    /// <summary>Approves the proposal and atomically executes the trade (coins + items).</summary>
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

        TradeOutcome outcome;
        try {
            outcome = await transactionRepo.ExecuteTrade(proposal);
        }
        catch (Exception ex) {
            // The claim already moved the proposal to InProgress; ensure it can't be stranded
            // there if execution throws (the trade's own SaveChanges rolled back, so nothing
            // moved). Move it to a terminal Failed state so it's auditable and unblocked.
            await proposalRepo.MarkFailed(id, "Trade could not be completed.");
            logger.LogError(ex, "Proposal {ProposalId} approved by user {UserId} but trade threw", id, userId);
            return Ok(new ConsentResultResponse {
                ProposalId    = id,
                Status        = TransactionProposalStatus.Failed.ToString(),
                FailureReason = "Trade could not be completed.",
                Redirect      = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Failed)
            });
        }

        if (!outcome.Success) {
            string reason = outcome.Error switch {
                TradeError.Empty                 => "The trade moves nothing.",
                TradeError.ItemNotFound          => "An item in the trade no longer exists.",
                TradeError.OfferedItemNotOwned   => "The app no longer owns an offered item.",
                TradeError.RequestedItemNotOwned => "You no longer own a requested item.",
                TradeError.InsufficientUserFunds => "Insufficient funds.",
                TradeError.InsufficientAppFunds  => "The app has insufficient funds.",
                TradeError.Overflow              => "A balance would overflow.",
                _                                => "Trade failed."
            };
            await proposalRepo.MarkFailed(id, reason);
            logger.LogInformation("Proposal {ProposalId} approved by user {UserId} but trade failed: {Reason}",
                id, userId, reason);
            return Ok(new ConsentResultResponse {
                ProposalId    = id,
                Status        = TransactionProposalStatus.Failed.ToString(),
                FailureReason = reason,
                Redirect      = BuildRedirect(proposal.RedirectUri, id, TransactionProposalStatus.Failed)
            });
        }

        // Record the requested-coins transaction id when present, else the offered-coins one
        // (items-only trades have neither — the approval itself is the audit record).
        string? txId = outcome.RequestedTransaction?.Id ?? outcome.OfferedTransaction?.Id;
        await proposalRepo.MarkApproved(id, txId);
        logger.LogInformation("Proposal {ProposalId} approved by user {UserId}; trade executed (tx {TxId})",
            id, userId, txId ?? "(items only)");
        return Ok(new ConsentResultResponse {
            ProposalId    = id,
            Status        = TransactionProposalStatus.Approved.ToString(),
            TransactionId = txId,
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
