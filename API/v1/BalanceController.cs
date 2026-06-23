using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1;

/// <summary>
/// Generic balance endpoints that act on the authenticated principal's own balance.
///
/// Owner resolution:
///   - app API key (auth_type = ApiKey)  → the app's balance.
///   - user token / OAuth delegated token → the user's balance (an app acting for a user
///     operates on that user's balance, provided it holds the economy scope).
///
/// Authorization uses the <c>EconomyAccess</c> policy: API-key apps pass automatically, while
/// user/OAuth principals must hold the <c>economy</c> scope.
/// </summary>
[ApiController]
[Route("api/v1/balance")]
[Authorize(Policy = "EconomyAccess")]
public class BalanceController(
    IBalanceRepository balanceRepo,
    ITransactionRepository transactionRepo,
    IUserRepository userRepo) : ControllerManager {

    public class BalanceResponse {
        public string Id { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public ulong Coins { get; set; }

        public static BalanceResponse From(Balance b) => new() {
            Id        = b.Id,
            OwnerType = b.OwnerType.ToString(),
            OwnerId   = b.OwnerId,
            Coins     = b.Coins
        };
    }

    public class TransactionResponse {
        public string Id { get; set; } = "";
        public string? FromBalanceId { get; set; }
        public string? ToBalanceId { get; set; }
        public ulong Amount { get; set; }
        public string? Description { get; set; }
        public DateTime DateCreated { get; set; }

        public static TransactionResponse From(Transaction t) => new() {
            Id            = t.Id,
            FromBalanceId = t.FromBalanceId,
            ToBalanceId   = t.ToBalanceId,
            Amount        = t.Amount,
            Description   = t.Description,
            DateCreated   = t.DateCreated
        };
    }

    public class TransferBody {
        /// <summary>The recipient user's username or storage id.</summary>
        public string Recipient { get; set; } = "";
        public ulong Amount { get; set; }
        public string? Description { get; set; }
    }

    public class TransferResponse {
        public TransactionResponse Transaction { get; set; } = null!;
        public BalanceResponse FromBalance { get; set; } = null!;
        public BalanceResponse ToBalance { get; set; } = null!;
    }

    /// <summary>Resolves the (ownerType, ownerId) the current principal acts on, or null.</summary>
    private (BalanceOwnerType type, string id)? ResolveOwner() {
        if (HttpContext.User.IsAppKey()) {
            string? appId = HttpContext.User.GetAppId();
            return appId == null ? null : (BalanceOwnerType.App, appId);
        }
        string? userId = HttpContext.User.GetUserId();
        return userId == null ? null : (BalanceOwnerType.User, userId);
    }

    [HttpGet]
    public async Task<ActionResult<BalanceResponse>> Get() {
        if (ResolveOwner() is not { } owner) return Unauthorized();
        Balance bal = await balanceRepo.GetBalance(owner.type, owner.id);
        return Ok(BalanceResponse.From(bal));
    }

    /// <summary>Lists the caller's transaction history (newest first) for auditing.</summary>
    [HttpGet("transactions")]
    public async Task<ActionResult<TransactionResponse[]>> GetTransactions([FromQuery] int limit = 50) {
        if (ResolveOwner() is not { } owner) return Unauthorized();
        limit = Math.Clamp(limit, 1, 200);
        Transaction[] txs = await transactionRepo.GetTransactionsForOwner(owner.type, owner.id, limit);
        return Ok(txs.Select(TransactionResponse.From).ToArray());
    }

    /// <summary>
    /// Sends coins from the caller's balance to a recipient user (looked up by username or id).
    /// Zero-sum: the same amount is deducted from the sender and credited to the recipient in a
    /// single atomic operation that also writes an audit record.
    /// </summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<TransferResponse>> Transfer([FromBody] TransferBody body) {
        if (ResolveOwner() is not { } owner) return Unauthorized();
        if (body.Amount == 0) return BadRequest("Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(body.Recipient)) return BadRequest("Recipient is required.");

        User? recipient = await userRepo.GetUser(body.Recipient)
                          ?? await userRepo.GetUserFromName(body.Recipient);
        if (recipient == null) return NotFound("Recipient user not found.");

        TransferOutcome outcome = await transactionRepo.Transfer(
            owner.type, owner.id,
            BalanceOwnerType.User, recipient.Id,
            body.Amount, body.Description);

        if (!outcome.Success) {
            return outcome.Error switch {
                TransferError.ZeroAmount        => BadRequest("Amount must be greater than zero."),
                TransferError.SameOwner         => BadRequest("Cannot transfer to yourself."),
                TransferError.InsufficientFunds => BadRequest("Insufficient funds."),
                TransferError.RecipientOverflow => BadRequest("Recipient balance would overflow."),
                _                               => BadRequest("Transfer failed.")
            };
        }

        return Ok(new TransferResponse {
            Transaction = TransactionResponse.From(outcome.Transaction!),
            FromBalance = BalanceResponse.From(outcome.FromBalance!),
            ToBalance   = BalanceResponse.From(outcome.ToBalance!)
        });
    }
}
