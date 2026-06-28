using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// Public, unauthenticated lookups for users: their public identity (so item ownership history can
/// name who owned an item) and the items they own. Items are world-readable, so anyone may list a
/// user's items by id or username — this powers the trade UI, where a proposer browses the other
/// user's inventory to pick what to request instead of typing item ids. Only minimal public fields
/// (id, username, readable id) are exposed; nothing sensitive.
/// </summary>
[ApiController]
[Route("api/v1/user")]
[AllowAnonymous]
public class UserPublicController(
    IUserRepository userRepo,
    IItemRepository itemRepo) : ControllerManager {

    public class PublicUserResponse {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        public string ReadableId { get; set; } = "";

        public static PublicUserResponse From(User u) => new() {
            Id         = u.Id,
            Username   = u.Username,
            ReadableId = WordId.Encode(u.Id)
        };
    }

    public class PublicItemResponse {
        public string Id { get; set; } = "";
        public string ReadableId { get; set; } = "";
        public string OwnerType { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string CreatorAppId { get; set; } = "";
        public DateTime DateCreated { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? IconUrl { get; set; }

        public static PublicItemResponse From(Item i) => new() {
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

    public class BatchPublicUsersBody {
        public string[] Ids { get; set; } = [];
    }

    /// <summary>Public profile for a user by id (or username).</summary>
    [HttpGet("{id}/public")]
    public async Task<ActionResult<PublicUserResponse>> GetPublic(string id) {
        User? user = await userRepo.GetUser(id) ?? await userRepo.GetUserFromName(id);
        if (user == null) return NotFound("User not found.");
        return Ok(PublicUserResponse.From(user));
    }

    /// <summary>
    /// Resolves many user ids to their public info in one request — used by clients (e.g. an item's
    /// ownership history) to name user owners without an N+1 of per-user lookups. Unknown ids are
    /// omitted.
    /// </summary>
    [HttpPost("public/batch")]
    public async Task<ActionResult<PublicUserResponse[]>> GetPublicBatch([FromBody] BatchPublicUsersBody body) {
        string[] ids = (body.Ids ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(200).ToArray();
        if (ids.Length == 0) return Ok(Array.Empty<PublicUserResponse>());
        User[] users = await userRepo.GetUsers(ids);
        return Ok(users.Select(PublicUserResponse.From).ToArray());
    }

    /// <summary>
    /// Lists the items owned by a user (by id or username), newest first, paginated. <c>limit</c>
    /// is clamped to 1–200 (default 50). Pass <c>search</c> to filter by item name. Public so a user
    /// can browse another user's inventory (e.g. to pick items to request in a trade).
    /// </summary>
    [HttpGet("{id}/items")]
    public async Task<ActionResult<PublicItemResponse[]>> GetItems(
        string id,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null) {
        User? user = await userRepo.GetUser(id) ?? await userRepo.GetUserFromName(id);
        if (user == null) return NotFound("User not found.");

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);

        Item[] items = await itemRepo.GetItemsForOwner(
            BalanceOwnerType.User, user.Id, limit, offset, null, search);
        return Ok(items.Select(PublicItemResponse.From).ToArray());
    }
}
