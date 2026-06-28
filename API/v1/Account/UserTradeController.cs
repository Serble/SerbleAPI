using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// User-to-user trades. A logged-in user proposes a trade (offering and/or requesting coins and
/// items) to another user, who then approves or denies it on the website. On approval both sides
/// move atomically. The controller-level <c>[Authorize(Policy = "UserOnly")]</c> requires a
/// logged-in user; only the parties to a trade can see or act on it.
/// </summary>
[ApiController]
[Route("api/v1/trades")]
[Authorize(Policy = "UserOnly")]
public class UserTradeController(
    ILogger<UserTradeController> logger,
    IUserTradeRepository tradeRepo,
    ITransactionRepository transactionRepo,
    IUserRepository userRepo,
    IItemRepository itemRepo) : ControllerManager {

    /// <summary>How long a trade stays open for the recipient to act before it expires.</summary>
    public static readonly TimeSpan TradeLifetime = TimeSpan.FromDays(7);

    public class ItemView {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? IconUrl { get; set; }
        public string? Description { get; set; }
    }

    public class CreateTradeBody {
        /// <summary>The recipient: a user's id or username. Required, and not yourself.</summary>
        public string ToUser { get; set; } = "";
        /// <summary>Coins you give the recipient (you → them). Optional; defaults to 0.</summary>
        public ulong OfferedCoins { get; set; }
        /// <summary>Coins you ask from the recipient (them → you). Optional; defaults to 0.</summary>
        public ulong RequestedCoins { get; set; }
        /// <summary>Ids of items you give the recipient (you must own them).</summary>
        public List<string>? OfferedItemIds { get; set; }
        /// <summary>Ids of items you ask from the recipient (they must own them).</summary>
        public List<string>? RequestedItemIds { get; set; }
        public string? Description { get; set; }
    }

    public class TradeResponse {
        public string Id { get; set; } = "";
        public string Status { get; set; } = "";
        /// <summary>"incoming" if the caller is the recipient (they decide), else "outgoing".</summary>
        public string Direction { get; set; } = "";
        public string FromUserId { get; set; } = "";
        public string FromUsername { get; set; } = "";
        public string ToUserId { get; set; } = "";
        public string ToUsername { get; set; } = "";
        public ulong OfferedCoins { get; set; }
        public ulong RequestedCoins { get; set; }
        public ItemView[] OfferedItems { get; set; } = [];
        public ItemView[] RequestedItems { get; set; } = [];
        public bool IsGift { get; set; }
        public string? Description { get; set; }
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    private async Task<ItemView> ResolveItem(string id, Dictionary<string, ItemView> cache) {
        if (cache.TryGetValue(id, out ItemView? cached)) return cached;
        Item? item = await itemRepo.GetItem(id);
        ItemView view = new() {
            Id = id, Name = item?.Name ?? id, IconUrl = item?.IconUrl, Description = item?.Description
        };
        cache[id] = view;
        return view;
    }

    private async Task<string> ResolveUsername(string id, Dictionary<string, string> cache) {
        if (cache.TryGetValue(id, out string? cached)) return cached;
        User? user = await userRepo.GetUser(id);
        string name = user?.Username ?? id;
        cache[id] = name;
        return name;
    }

    private async Task<TradeResponse> ToResponse(UserTrade t, string callerId,
        Dictionary<string, string> userCache, Dictionary<string, ItemView> itemCache) {
        List<ItemView> offered = new();
        foreach (string id in t.OfferedItemIds) offered.Add(await ResolveItem(id, itemCache));
        List<ItemView> requested = new();
        foreach (string id in t.RequestedItemIds) requested.Add(await ResolveItem(id, itemCache));
        return new TradeResponse {
            Id             = t.Id,
            Status         = t.Status.ToString(),
            Direction      = t.ToUserId == callerId ? "incoming" : "outgoing",
            FromUserId     = t.FromUserId,
            FromUsername   = await ResolveUsername(t.FromUserId, userCache),
            ToUserId       = t.ToUserId,
            ToUsername     = await ResolveUsername(t.ToUserId, userCache),
            OfferedCoins   = t.OfferedCoins,
            RequestedCoins = t.RequestedCoins,
            OfferedItems   = offered.ToArray(),
            RequestedItems = requested.ToArray(),
            IsGift         = t.IsGift,
            Description    = t.Description,
            TransactionId  = t.TransactionId,
            FailureReason  = t.FailureReason,
            CreatedAt      = t.CreatedAt,
            ExpiresAt      = t.ExpiresAt,
            ResolvedAt     = t.ResolvedAt
        };
    }

    /// <summary>Proposes a trade to another user.</summary>
    [HttpPost]
    public async Task<ActionResult<TradeResponse>> Create([FromBody] CreateTradeBody body) {
        string? fromUserId = HttpContext.User.GetUserId();
        if (fromUserId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.ToUser)) return BadRequest("Recipient (toUser) is required.");
        User? toUser = await userRepo.GetUser(body.ToUser) ?? await userRepo.GetUserFromName(body.ToUser);
        if (toUser == null) return NotFound("Recipient user not found.");
        if (toUser.Id == fromUserId) return BadRequest("You cannot trade with yourself.");

        List<string> offeredItemIds = (body.OfferedItemIds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        List<string> requestedItemIds = (body.RequestedItemIds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();

        if (body.OfferedCoins == 0 && body.RequestedCoins == 0
            && offeredItemIds.Count == 0 && requestedItemIds.Count == 0)
            return BadRequest("A trade must move at least one asset (coins or items).");

        // You can only offer items you currently own.
        foreach (string itemId in offeredItemIds) {
            Item? item = await itemRepo.GetItem(itemId);
            if (item == null) return NotFound($"Offered item '{itemId}' not found.");
            if (item.OwnerType != BalanceOwnerType.User || item.OwnerId != fromUserId)
                return BadRequest($"You do not own offered item '{itemId}'.");
        }
        // Requested items must exist and belong to the recipient (re-checked at approval too).
        foreach (string itemId in requestedItemIds) {
            Item? item = await itemRepo.GetItem(itemId);
            if (item == null) return NotFound($"Requested item '{itemId}' not found.");
            if (item.OwnerType != BalanceOwnerType.User || item.OwnerId != toUser.Id)
                return BadRequest($"Requested item '{itemId}' is not owned by {toUser.Username}.");
        }

        DateTime now = DateTime.UtcNow;
        UserTrade trade = new() {
            Id               = OidcCrypto.NewHandle(),
            FromUserId       = fromUserId,
            ToUserId         = toUser.Id,
            OfferedCoins     = body.OfferedCoins,
            RequestedCoins   = body.RequestedCoins,
            OfferedItemIds   = offeredItemIds,
            RequestedItemIds = requestedItemIds,
            Description      = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            Status           = UserTradeStatus.Pending,
            CreatedAt        = now,
            ExpiresAt        = now + TradeLifetime
        };
        await tradeRepo.Create(trade);
        logger.LogInformation("User {From} proposed trade {Id} to {To}", fromUserId, trade.Id, toUser.Id);

        Dictionary<string, string> userCache = new();
        Dictionary<string, ItemView> itemCache = new();
        return Ok(await ToResponse(trade, fromUserId, userCache, itemCache));
    }

    /// <summary>
    /// Lists the caller's trades (newest first). <c>role</c> = <c>incoming</c> (proposed to you),
    /// <c>outgoing</c> (proposed by you), or <c>all</c> (default). Optional <c>status</c> filters by
    /// lifecycle (Pending/Approved/Denied/Cancelled/Expired/Failed).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TradeResponse[]>> List(
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        UserTradeRole roleFilter = (role?.ToLowerInvariant()) switch {
            "incoming" => UserTradeRole.Incoming,
            "outgoing" => UserTradeRole.Outgoing,
            _          => UserTradeRole.Any
        };
        UserTradeStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status)) {
            if (!Enum.TryParse(status, true, out UserTradeStatus parsed))
                return BadRequest("Invalid status filter.");
            statusFilter = parsed;
        }

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        UserTrade[] trades = await tradeRepo.ListForUser(userId, roleFilter, statusFilter, limit, offset);
        Dictionary<string, string> userCache = new();
        Dictionary<string, ItemView> itemCache = new();
        List<TradeResponse> result = new();
        foreach (UserTrade t in trades) result.Add(await ToResponse(t, userId, userCache, itemCache));
        return Ok(result.ToArray());
    }

    /// <summary>Gets one trade the caller is a party to.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TradeResponse>> Get(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        UserTrade? trade = await tradeRepo.GetById(id);
        if (trade == null || (trade.FromUserId != userId && trade.ToUserId != userId))
            return NotFound("Trade not found.");
        return Ok(await ToResponse(trade, userId, new(), new()));
    }

    /// <summary>Approves an incoming trade and atomically executes the swap. Recipient only.</summary>
    [HttpPost("{id}/approve")]
    public async Task<ActionResult<TradeResponse>> Approve(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        UserTrade? existing = await tradeRepo.GetById(id);
        if (existing == null || existing.ToUserId != userId) return NotFound("Trade not found.");
        if (existing.Status != UserTradeStatus.Pending)
            return BadRequest($"Trade is not pending (status: {existing.Status}).");

        UserTrade? trade = await tradeRepo.TryBeginConsent(id, userId);
        if (trade == null) return BadRequest("Trade could not be approved (it may have just been resolved or expired).");

        TradeOutcome outcome;
        try {
            outcome = await transactionRepo.ExecuteUserTrade(trade);
        }
        catch (Exception ex) {
            await tradeRepo.MarkFailed(id, "Trade could not be completed.");
            logger.LogError(ex, "User trade {Id} approved by {UserId} but execution threw", id, userId);
            return Ok(await ReloadResponse(id, userId));
        }

        if (!outcome.Success) {
            string reason = outcome.Error switch {
                TradeError.Empty                        => "The trade moves nothing.",
                TradeError.ItemNotFound                 => "An item in the trade no longer exists.",
                TradeError.OfferedItemNotOwned          => "The sender no longer owns an offered item.",
                TradeError.RequestedItemNotOwned        => "You no longer own a requested item.",
                TradeError.InsufficientInitiatorFunds   => "The sender has insufficient funds.",
                TradeError.InsufficientCounterpartyFunds => "You have insufficient funds.",
                TradeError.Overflow                     => "A balance would overflow.",
                _                                       => "Trade failed."
            };
            await tradeRepo.MarkFailed(id, reason);
            logger.LogInformation("User trade {Id} approved by {UserId} but failed: {Reason}", id, userId, reason);
            return Ok(await ReloadResponse(id, userId));
        }

        string? txId = outcome.OfferedTransaction?.Id ?? outcome.RequestedTransaction?.Id;
        await tradeRepo.MarkApproved(id, txId);
        logger.LogInformation("User trade {Id} approved by {UserId}; executed (tx {TxId})",
            id, userId, txId ?? "(items only)");
        return Ok(await ReloadResponse(id, userId));
    }

    /// <summary>Declines an incoming trade. Recipient only.</summary>
    [HttpPost("{id}/deny")]
    public async Task<ActionResult<TradeResponse>> Deny(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        UserTrade? trade = await tradeRepo.GetById(id);
        if (trade == null || trade.ToUserId != userId) return NotFound("Trade not found.");
        if (trade.Status != UserTradeStatus.Pending)
            return BadRequest($"Trade is not pending (status: {trade.Status}).");

        if (!await tradeRepo.MarkDenied(id, userId))
            return BadRequest("Trade could not be denied (it may have just been resolved).");
        logger.LogInformation("User trade {Id} denied by {UserId}", id, userId);
        return Ok(await ReloadResponse(id, userId));
    }

    /// <summary>Withdraws a still-pending outgoing trade. Initiator only.</summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<TradeResponse>> Cancel(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        UserTrade? trade = await tradeRepo.GetById(id);
        if (trade == null || trade.FromUserId != userId) return NotFound("Trade not found.");
        if (trade.Status != UserTradeStatus.Pending)
            return BadRequest($"Trade is not pending (status: {trade.Status}).");

        if (!await tradeRepo.MarkCancelled(id, userId))
            return BadRequest("Trade could not be cancelled (it may have just been resolved).");
        logger.LogInformation("User trade {Id} cancelled by {UserId}", id, userId);
        return Ok(await ReloadResponse(id, userId));
    }

    private async Task<TradeResponse> ReloadResponse(string id, string callerId) {
        UserTrade updated = (await tradeRepo.GetById(id))!;
        return await ToResponse(updated, callerId, new(), new());
    }
}
