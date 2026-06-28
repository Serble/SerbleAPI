using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>
/// Read access to item ownership history (the audit trail). Records are <b>written</b> inline by the
/// operations that move items (item creation in <see cref="Impl.ItemRepository"/> and trade
/// execution in <see cref="Impl.TransactionRepository"/>) so they are part of the same atomic save;
/// this interface only exposes the queries used to look that history up.
/// </summary>
public interface IItemTransactionRepository {
    /// <summary>Ownership history for an item (newest first), paginated.</summary>
    Task<ItemTransaction[]> GetHistoryForItem(string itemId, int limit, int offset = 0);

    /// <summary>Total number of ownership records for an item (for pagination).</summary>
    Task<int> CountHistoryForItem(string itemId);
}
