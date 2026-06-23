using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IBalanceRepository {
    /// <summary>
    /// Returns the owner's default balance. If no row exists yet a transient zero-balance
    /// (not persisted) is returned, so reads never create rows.
    /// </summary>
    Task<Balance> GetBalance(BalanceOwnerType ownerType, string ownerId);
    
    /// <summary>Returns a specific balance by its id, or null if it does not exist.</summary>
    Task<Balance?> GetBalanceById(string balanceId);

    /// <summary>Returns the balances matching the given ids (missing ids are omitted).</summary>
    Task<Balance[]> GetBalancesByIds(IEnumerable<string> ids);
    
    /// <summary>Sets the owner's default balance to an absolute value, creating the row if needed.</summary>
    Task<Balance> SetBalance(BalanceOwnerType ownerType, string ownerId, ulong coins);
    
    /// <summary>Adds coins to the owner's default balance (saturates at ulong.MaxValue).</summary>
    Task<Balance> AddCoins(BalanceOwnerType ownerType, string ownerId, ulong amount);
    
    /// <summary>Removes coins from the owner's default balance (clamped at 0).</summary>
    Task<Balance> RemoveCoins(BalanceOwnerType ownerType, string ownerId, ulong amount);
    
    /// <summary>Deletes every balance owned by the given entity.</summary>
    Task DeleteBalancesForOwner(BalanceOwnerType ownerType, string ownerId);
}
