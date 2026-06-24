using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// App-facing endpoints for proposing a coin transaction that a user must consent to on the
/// serble website (modelled on the OAuth consent flow). The controller-level
/// <c>[Authorize(Policy = "AppOnly")]</c> requires pure app-key auth — any app authenticating
/// with its own API key may propose. The consenting user is always the payer; the proposing app
/// names the recipient (itself, another app, or another user).
///
/// Lifecycle: create → user approves/denies on the website → the app polls <c>GET {id}</c> to
/// confirm whether the transaction was made.
/// </summary>
[ApiController]
[Route("api/v1/transactions/proposals")]
[Authorize(Policy = "AppOnly")]
public class TransactionProposalController(
    ILogger<TransactionProposalController> logger,
    ITransactionProposalRepository proposalRepo,
    IUserRepository userRepo,
    IAppRepository appRepo,
    IOptions<ApiSettings> apiSettings) : ControllerManager {

    /// <summary>How long a proposal stays open for the user to act before it expires.</summary>
    public static readonly TimeSpan ProposalLifetime = TimeSpan.FromMinutes(15);

    public class CreateProposalBody {
        /// <summary>The payer: a user's id or username. Required.</summary>
        public string User { get; set; } = "";
        /// <summary>Recipient owner kind: "User" or "App". Defaults to "App" (the proposing app).</summary>
        public string? RecipientType { get; set; }
        /// <summary>Recipient identifier (user id/username, or app id). Defaults to the proposing app.</summary>
        public string? Recipient { get; set; }
        public ulong Amount { get; set; }
        public string? Description { get; set; }
        /// <summary>Optional URL to return the user to after they decide. Any URL is accepted;
        /// only the opaque proposal id and outcome status are appended to it.</summary>
        public string? RedirectUri { get; set; }
    }

    public class ProposalResponse {
        public string ProposalId { get; set; } = "";
        public string Status { get; set; } = "";
        public string AppId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string RecipientType { get; set; } = "";
        public string RecipientId { get; set; } = "";
        public ulong Amount { get; set; }
        public string? Description { get; set; }
        public string? RedirectUri { get; set; }
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public static ProposalResponse From(TransactionProposal p) => new() {
            ProposalId    = p.Id,
            Status        = p.Status.ToString(),
            AppId         = p.AppId,
            UserId        = p.UserId,
            RecipientType = p.RecipientType.ToString(),
            RecipientId   = p.RecipientId,
            Amount        = p.Amount,
            Description   = p.Description,
            RedirectUri   = p.RedirectUri,
            TransactionId = p.TransactionId,
            FailureReason = p.FailureReason,
            CreatedAt     = p.CreatedAt,
            ExpiresAt     = p.ExpiresAt,
            ResolvedAt    = p.ResolvedAt
        };
    }

    public class CreateProposalResponse {
        public ProposalResponse Proposal { get; set; } = null!;
        public string ConsentUrl { get; set; } = "";
    }

    [HttpPost]
    public async Task<ActionResult<CreateProposalResponse>> Create([FromBody] CreateProposalBody body) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        if (body.Amount == 0) return BadRequest("Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(body.User)) return BadRequest("User (payer) is required.");

        User? payer = await userRepo.GetUser(body.User) ?? await userRepo.GetUserFromName(body.User);
        if (payer == null) return NotFound("Payer user not found.");

        // Recipient defaults to the proposing app.
        string recipientTypeRaw = string.IsNullOrWhiteSpace(body.RecipientType) ? "App" : body.RecipientType.Trim();
        if (!Enum.TryParse(recipientTypeRaw, true, out BalanceOwnerType recipientType))
            return BadRequest("RecipientType must be 'User' or 'App'.");

        string recipientId;
        switch (recipientType) {
            case BalanceOwnerType.App: {
                string target = string.IsNullOrWhiteSpace(body.Recipient) ? appId : body.Recipient.Trim();
                OAuthApp? app = await appRepo.GetOAuthApp(target);
                if (app == null) return NotFound("Recipient app not found.");
                recipientId = app.Id;
                break;
            }
            case BalanceOwnerType.User: {
                if (string.IsNullOrWhiteSpace(body.Recipient))
                    return BadRequest("Recipient is required when RecipientType is 'User'.");
                User? recipientUser = await userRepo.GetUser(body.Recipient) ?? await userRepo.GetUserFromName(body.Recipient);
                if (recipientUser == null) return NotFound("Recipient user not found.");
                recipientId = recipientUser.Id;
                break;
            }
            default:
                return BadRequest("RecipientType must be 'User' or 'App'.");
        }

        if (recipientType == BalanceOwnerType.User && recipientId == payer.Id)
            return BadRequest("Payer and recipient cannot be the same user.");

        // Optional post-decision redirect. Set by the app via its own API key; nothing sensitive
        // is passed through it (only the opaque proposal id and outcome status), so it is not
        // restricted to the app's registered redirect URIs.
        string? redirectUri = string.IsNullOrWhiteSpace(body.RedirectUri) ? null : body.RedirectUri.Trim();

        DateTime now = DateTime.UtcNow;
        TransactionProposal proposal = new() {
            Id            = OidcCrypto.NewHandle(),
            AppId         = appId,
            UserId        = payer.Id,
            RecipientType = recipientType,
            RecipientId   = recipientId,
            Amount        = body.Amount,
            Description   = body.Description,
            RedirectUri   = redirectUri,
            Status        = TransactionProposalStatus.Pending,
            CreatedAt     = now,
            ExpiresAt     = now + ProposalLifetime
        };
        await proposalRepo.Create(proposal);

        string consentUrl = QueryHelpers.AddQueryString(
            apiSettings.Value.WebsiteUrl.TrimEnd('/') + "/transactions/consent",
            "proposal", proposal.Id);

        logger.LogInformation("App {AppId} proposed transaction {ProposalId}: user {UserId} -> {RecipientType}:{RecipientId} ({Amount})",
            appId, proposal.Id, payer.Id, recipientType, recipientId, body.Amount);

        return Ok(new CreateProposalResponse {
            Proposal   = ProposalResponse.From(proposal),
            ConsentUrl = consentUrl
        });
    }

    /// <summary>
    /// Polls a proposal's status. Scoped to the calling app — an app can only see its own
    /// proposals. This is how the app confirms whether the transaction was actually made
    /// (<c>Approved</c> with a <c>TransactionId</c>) or not (<c>Denied</c>/<c>Failed</c>/etc.).
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ProposalResponse>> Get(string id) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        TransactionProposal? proposal = await proposalRepo.GetById(id);
        if (proposal == null || proposal.AppId != appId) return NotFound("Proposal not found.");
        return Ok(ProposalResponse.From(proposal));
    }

    /// <summary>Withdraws a still-pending proposal (scoped to the calling app).</summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ProposalResponse>> Cancel(string id) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        TransactionProposal? proposal = await proposalRepo.GetById(id);
        if (proposal == null || proposal.AppId != appId) return NotFound("Proposal not found.");
        if (proposal.Status != TransactionProposalStatus.Pending)
            return BadRequest($"Proposal is not pending (status: {proposal.Status}).");

        if (!await proposalRepo.MarkCancelled(id))
            return BadRequest("Proposal could not be cancelled (it may have just been resolved).");

        TransactionProposal? updated = await proposalRepo.GetById(id);
        return Ok(ProposalResponse.From(updated!));
    }
}
