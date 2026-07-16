using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// The authenticated user's item inventory. Guarded by the <c>economy</c> scope, so it is
/// reachable both by a logged-in user (full access) and by an OAuth app acting for the user with
/// the <c>economy</c> scope. App-key (app-as-itself) requests have no associated user and are
/// rejected — an app reads its own items via <c>GET /api/v1/items</c> instead.
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Authorize(Policy = "Scope:Economy")]
[RequireFeature(FeatureFlagCatalog.Economy)]
public class InventoryController(IItemRepository itemRepo, IAppRepository appRepo) : ControllerManager {

    public class InventoryItemResponse {
        public string Id { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string CreatorAppId { get; set; } = "";
        public bool CreatorAppIsOfficial { get; set; }
        public DateTime DateCreated { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }

        public static InventoryItemResponse From(Item i, bool creatorAppIsOfficial = false) => new() {
            Id                  = i.Id,
            OwnerType           = i.OwnerType.ToString(),
            OwnerId             = i.OwnerId,
            CreatorAppId        = i.CreatorAppId,
            CreatorAppIsOfficial = creatorAppIsOfficial,
            DateCreated         = i.DateCreated,
            Name                = i.Name,
            Description         = i.Description,
            IconUrl             = i.IconUrl
        };
    }

    /// <summary>
    /// Lists the user's items (newest first), paginated. Pass <c>creatorApp</c> to return only the
    /// items created by that app — used by embeddable item views so an app can show just its own
    /// items from the user's inventory. Pass <c>search</c> to filter by item name (case-insensitive
    /// substring), so large inventories can be searched server-side.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<InventoryItemResponse[]>> List(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? creatorApp = null,
        [FromQuery] string? search = null) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        Item[] items = await itemRepo.GetItemsForOwner(
            BalanceOwnerType.User, userId, limit, offset, creatorApp, search);
        string[] creatorAppIds = items
            .Select(i => i.CreatorAppId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();
        OAuthApp[] creatorApps = await appRepo.GetOAuthApps(creatorAppIds);
        Dictionary<string, bool> creatorOfficial = creatorApps.ToDictionary(a => a.Id, a => a.IsOfficial);

        return Ok(items.Select(i => {
            bool isOfficial = creatorOfficial.TryGetValue(i.CreatorAppId, out bool val) && val;
            return InventoryItemResponse.From(i, isOfficial);
        }).ToArray());
    }

    /// <summary>Gets one item the user owns.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<InventoryItemResponse>> Get(string id) {
        string? userId = HttpContext.User.GetUserId();
        if (userId == null) return Unauthorized();

        Item? item = await itemRepo.GetItem(id);
        if (item == null || item.OwnerType != BalanceOwnerType.User || item.OwnerId != userId)
            return NotFound("Item not found.");
        OAuthApp? creatorApp = await appRepo.GetOAuthApp(item.CreatorAppId);
        return Ok(InventoryItemResponse.From(item, creatorApp?.IsOfficial == true));
    }
}
