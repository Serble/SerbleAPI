using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// App-facing CRUD for items, authenticated purely with an app API key (controller-level
/// <c>[Authorize(Policy = "AppOnly")]</c>). A newly created item is owned by the calling app, and
/// an app can only see the items it owns through these routes. Item icons must use a URL whose
/// prefix is allowed by the <see cref="ServerConfigCatalog.AllowedIconUrlPrefixes"/> setting.
/// Minting an item may cost coins — see the server-wide item-creation fee
/// (<see cref="ServerConfigCatalog.ItemCreationFee"/>).
/// </summary>
[ApiController]
[Route("api/v1/items")]
[Authorize(Policy = "AppOnly")]
public class ItemController(
    ILogger<ItemController> logger,
    IItemRepository itemRepo,
    IBalanceRepository balanceRepo,
    IServerConfigService serverConfig) : ControllerManager {

    public class CreateItemBody {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
    }

    public class ItemFeeResponse {
        /// <summary>The fee as a decimal coin amount (as configured, e.g. "0.5").</summary>
        public string Coins { get; set; } = "0";
        /// <summary>The fee in raw fixed-point units (what is actually deducted from the balance).</summary>
        public ulong Raw { get; set; }
    }

    public class ItemResponse {
        public string Id { get; set; } = "";
        public string ReadableId { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string CreatorAppId { get; set; } = "";
        public DateTime DateCreated { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }

        public static ItemResponse From(Item i) => new() {
            Id           = i.Id,
            ReadableId   = WordId.Encode(i.Id),
            OwnerType    = i.OwnerType.ToString(),
            OwnerId      = i.OwnerId,
            CreatorAppId = i.CreatorAppId,
            DateCreated  = i.DateCreated,
            Name         = i.Name,
            Description  = i.Description,
            IconUrl      = i.IconUrl
        };
    }

    /// <summary>
    /// Returns the current server-wide item-creation fee, so an app can check the cost before
    /// minting (and surface it to its users).
    /// </summary>
    [HttpGet("fee")]
    public async Task<ActionResult<ItemFeeResponse>> GetFee() {
        ItemCreationFee fee = await serverConfig.GetItemCreationFee();
        return Ok(new ItemFeeResponse { Coins = fee.Coins, Raw = fee.Raw });
    }

    /// <summary>
    /// Creates an item owned by the calling app. If a server-wide item-creation fee is configured,
    /// the app is charged that many coins (burned from its balance); the request is rejected with
    /// <c>402</c> if the app cannot afford it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ItemResponse>> Create([FromBody] CreateItemBody body) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest("Name is required.");
        if (body.Name.Length > 128) return BadRequest("Name cannot be longer than 128 characters.");
        if (body.Description is { Length: > 1024 })
            return BadRequest("Description cannot be longer than 1024 characters.");

        string? iconUrl = string.IsNullOrWhiteSpace(body.IconUrl) ? null : body.IconUrl.Trim();
        string[] allowedPrefixes = await serverConfig.GetStringList(ServerConfigCatalog.AllowedIconUrlPrefixes);
        bool iconAllowed = iconUrl == null
            || allowedPrefixes.Any(p => iconUrl.StartsWith(p, StringComparison.Ordinal));
        if (!iconAllowed)
            return BadRequest("IconUrl must start with an allowed prefix.");

        // Resolve the creation fee up front and verify the app can afford it before minting.
        ItemCreationFee fee = await serverConfig.GetItemCreationFee();
        ulong feeRaw = fee.Raw;
        if (feeRaw > 0) {
            Balance balance = await balanceRepo.GetBalance(BalanceOwnerType.App, appId);
            if (balance.Coins < feeRaw)
                return StatusCode(StatusCodes.Status402PaymentRequired,
                    $"Insufficient funds: minting an item costs {fee.Coins} coins.");
        }

        Item item = new() {
            Id           = OidcCrypto.NewHandle(),
            OwnerType    = BalanceOwnerType.App,
            OwnerId      = appId,
            CreatorAppId = appId,
            DateCreated  = DateTime.UtcNow,
            Name         = body.Name.Trim(),
            Description  = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            IconUrl      = iconUrl
        };
        await itemRepo.CreateItem(item);

        // Charge the fee after the item exists (burn from the app's balance). RemoveCoins clamps at
        // zero, so a concurrent spend can never drive the balance negative.
        if (feeRaw > 0) {
            await balanceRepo.RemoveCoins(BalanceOwnerType.App, appId, feeRaw,
                $"Item creation fee: {item.Id}");
            logger.LogInformation("App {AppId} charged {Fee} coins for item {ItemId}",
                appId, fee.Coins, item.Id);
        }

        logger.LogInformation("App {AppId} created item {ItemId}", appId, item.Id);
        return Ok(ItemResponse.From(item));
    }

    /// <summary>Lists items owned by the calling app (newest first), paginated.</summary>
    [HttpGet]
    public async Task<ActionResult<ItemResponse[]>> List(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        Item[] items = await itemRepo.GetItemsForOwner(BalanceOwnerType.App, appId, limit, offset);
        return Ok(items.Select(ItemResponse.From).ToArray());
    }

    /// <summary>Gets one item the calling app owns.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ItemResponse>> Get(string id) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        Item? item = await itemRepo.GetItem(id);
        if (item == null || item.OwnerType != BalanceOwnerType.App || item.OwnerId != appId)
            return NotFound("Item not found.");
        return Ok(ItemResponse.From(item));
    }
}
