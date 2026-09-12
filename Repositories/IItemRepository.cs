using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>Why an item could not be created (or <see cref="None"/> on success).</summary>
public enum ItemCreationError {
    None = 0,
    /// <summary>The owner could not pay the creation fee in full.</summary>
    InsufficientFunds
}

/// <summary>Result of an attempted item creation that had to be paid for.</summary>
public class ItemCreationOutcome {
    public bool Success => Error == ItemCreationError.None;
    public ItemCreationError Error { get; init; }

    public static ItemCreationOutcome Ok() => new() { Error = ItemCreationError.None };

    public static ItemCreationOutcome Fail(ItemCreationError error) => new() { Error = error };
}

public interface IItemRepository {
    /// <summary>Persists a new item.</summary>
    Task CreateItem(Item item);

    /// <summary>
    /// Atomically charges <paramref name="fee"/> to the item's owner and creates the item, in that
    /// order and in a single transaction: either the owner paid the fee in full and the item exists,
    /// or nothing happened. Fails without creating anything when the owner cannot afford it.
    /// <para>
    /// Creating the item first and charging afterwards is not equivalent. The charge would run
    /// against a balance that may have been spent in the meantime, so concurrent creations each pass
    /// the same affordability check, and a clamped debit quietly settles for whatever is left — the
    /// fee becomes optional under load.
    /// </para>
    /// A <paramref name="fee"/> of zero is a plain creation.
    /// </summary>
    Task<ItemCreationOutcome> CreateItemWithFee(Item item, ulong fee, string? feeDescription);

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
