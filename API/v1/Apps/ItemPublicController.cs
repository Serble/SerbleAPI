using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// Public, unauthenticated lookups for a single item: its profile and its ownership history. Items
/// are world-readable so anyone with an item id can inspect what it is and how it has changed hands
/// (its chain of custody). Owner-scoped reads/writes live on <see cref="ItemController"/>
/// (app key) and the inventory controller (user); this controller only exposes read-only public
/// views and shares the <c>api/v1/items</c> prefix without colliding with those routes.
/// </summary>
[ApiController]
[Route("api/v1/items")]
[AllowAnonymous]
public class ItemPublicController(
    IItemRepository itemRepo,
    IItemTransactionRepository historyRepo) : ControllerManager {

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

    public class ItemHistoryEntry {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? FromOwnerType { get; set; }
        public string? FromOwnerId { get; set; }
        public string ToOwnerType { get; set; } = "";
        public string ToOwnerId { get; set; } = "";
        public string? ProposalId { get; set; }
        public DateTime DateCreated { get; set; }

        public static ItemHistoryEntry From(ItemTransaction t) => new() {
            Id            = t.Id,
            Kind          = t.Kind.ToString(),
            FromOwnerType = t.FromOwnerType?.ToString(),
            FromOwnerId   = t.FromOwnerId,
            ToOwnerType   = t.ToOwnerType.ToString(),
            ToOwnerId     = t.ToOwnerId,
            ProposalId    = t.ProposalId,
            DateCreated   = t.DateCreated
        };
    }

    public class ItemHistoryResponse {
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
        public ItemHistoryEntry[] Entries { get; set; } = [];
    }

    /// <summary>Public profile for any item by id.</summary>
    [HttpGet("{id}/public")]
    public async Task<ActionResult<PublicItemResponse>> GetPublic(string id) {
        Item? item = await itemRepo.GetItem(id);
        if (item == null) return NotFound("Item not found.");
        return Ok(PublicItemResponse.From(item));
    }

    /// <summary>
    /// Public ownership history for any item (newest first), paginated. <c>limit</c> is clamped to
    /// 1–100 (default 25). The response carries the total count so clients can paginate.
    /// </summary>
    [HttpGet("{id}/history")]
    public async Task<ActionResult<ItemHistoryResponse>> GetHistory(
        string id,
        [FromQuery] int limit = 25,
        [FromQuery] int offset = 0) {
        Item? item = await itemRepo.GetItem(id);
        if (item == null) return NotFound("Item not found.");

        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        int total = await historyRepo.CountHistoryForItem(id);
        ItemTransaction[] history = await historyRepo.GetHistoryForItem(id, limit, offset);
        return Ok(new ItemHistoryResponse {
            Total   = total,
            Limit   = limit,
            Offset  = offset,
            Entries = history.Select(ItemHistoryEntry.From).ToArray()
        });
    }
}
