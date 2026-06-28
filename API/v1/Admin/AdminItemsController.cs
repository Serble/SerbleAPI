using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Admin-only read access to app-created items. The controller-level
/// <c>[Authorize(Policy = "AdminOnly")]</c> ensures the caller is an admin user-token holder.
/// Supports auditing any item regardless of owner (App or User).
/// </summary>
[ApiController]
[Route("api/v1/admin/items")]
[Authorize(Policy = "AdminOnly")]
public class AdminItemsController(
    IItemRepository itemRepo,
    IUserRepository userRepo,
    IAppRepository appRepo) : ControllerManager {

    public class AdminItemView {
        public string Id { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string CreatorAppId { get; set; } = "";
        public DateTime DateCreated { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }

        public static AdminItemView From(Item i) => new() {
            Id           = i.Id,
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
    /// Lists items for auditing, newest first. Optional filters combine (AND):
    ///   <c>ownerType</c> + <c>owner</c> — items owned by a specific owner (owner accepts a user
    ///     id/username when ownerType is "User", or an app id when "App");
    ///   <c>creatorApp</c> — items created by a specific app id.
    /// Paginated via <c>limit</c> (1..200, default 50) and <c>offset</c>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AdminItemView[]>> Get(
        [FromQuery] string? ownerType = null,
        [FromQuery] string? owner = null,
        [FromQuery] string? creatorApp = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0) {

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        (BalanceOwnerType type, string id)? ownerFilter = null;

        if (!string.IsNullOrWhiteSpace(ownerType) || !string.IsNullOrWhiteSpace(owner)) {
            if (string.IsNullOrWhiteSpace(ownerType) || string.IsNullOrWhiteSpace(owner))
                return BadRequest("Both ownerType and owner must be provided together.");
            if (!Enum.TryParse(ownerType.Trim(), true, out BalanceOwnerType parsedType))
                return BadRequest("ownerType must be 'User' or 'App'.");

            switch (parsedType) {
                case BalanceOwnerType.User: {
                    User? u = await userRepo.GetUser(owner) ?? await userRepo.GetUserFromName(owner);
                    if (u == null) return NotFound($"User '{owner}' not found.");
                    ownerFilter = (BalanceOwnerType.User, u.Id);
                    break;
                }
                case BalanceOwnerType.App: {
                    OAuthApp? app = await appRepo.GetOAuthApp(owner);
                    if (app == null) return NotFound($"App '{owner}' not found.");
                    ownerFilter = (BalanceOwnerType.App, app.Id);
                    break;
                }
                default:
                    return BadRequest("ownerType must be 'User' or 'App'.");
            }
        }

        string? creatorFilter = null;
        if (!string.IsNullOrWhiteSpace(creatorApp)) {
            OAuthApp? app = await appRepo.GetOAuthApp(creatorApp);
            if (app == null) return NotFound($"App '{creatorApp}' not found.");
            creatorFilter = app.Id;
        }

        Item[] items = await itemRepo.QueryItems(ownerFilter, creatorFilter, limit, offset);
        return Ok(items.Select(AdminItemView.From).ToArray());
    }

    /// <summary>Gets any item by id.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AdminItemView>> GetById(string id) {
        Item? item = await itemRepo.GetItem(id);
        if (item == null) return NotFound("Item not found.");
        return Ok(AdminItemView.From(item));
    }
}
