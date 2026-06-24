using System.Numerics;

namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Aggregate view of the total coin value in circulation across every balance in the
/// database, with a breakdown by owner type. Totals are <see cref="BigInteger"/> because the
/// sum of many <c>ulong</c> balances can exceed <c>ulong.MaxValue</c>.
/// </summary>
public class EconomyTotal {
    /// <summary>Total coins summed across all balances (users + apps + any future owner types).</summary>
    public BigInteger TotalCoins { get; set; }

    /// <summary>Total coins held by user-owned balances.</summary>
    public BigInteger UserCoins { get; set; }

    /// <summary>Total coins held by app-owned balances.</summary>
    public BigInteger AppCoins { get; set; }

    /// <summary>Number of balance rows that were summed.</summary>
    public long BalanceCount { get; set; }
}
