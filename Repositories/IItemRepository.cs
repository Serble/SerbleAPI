using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IItemRepository {
    /// <summary>Persists a new item.</summary>
    Task CreateItem(Item item);

    /// <summary>Returns an item by id, or null if it doesn't exist.</summary>
    Task<Item?> GetItem(string id);

    /// <summary>
    /// Returns items owned by a specific owner (newest first), paginated. When
    /// <paramref name="creatorAppId"/> is supplied, only items created by that app are returned.
    /// When <paramref name="search"/> is supplied, only items whose name contains it (case-
    /// insensitive) are returned, so large inventories can be searched server-side.
    /// </summary>
    Task<Item[]> GetItemsForOwner(BalanceOwnerType ownerType, string ownerId, int limit, int offset = 0,
        string? creatorAppId = null, string? search = null);

    /// <summary>Returns items created by a specific app (newest first), paginated.</summary>
    Task<Item[]> GetItemsCreatedByApp(string appId, int limit, int offset = 0);

    /// <summary>
    /// Admin query. Optional filters combine (AND): owner kind/id and creator app. Results are
    /// newest first and paginated.
    /// </summary>
    Task<Item[]> QueryItems(
        (BalanceOwnerType type, string id)? owner,
        string? creatorAppId,
        int limit, int offset);
}
