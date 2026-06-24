namespace SerbleAPI.Data;

/// <summary>
/// Coin balances are stored as unsigned <b>fixed-point</b> integers with
/// <see cref="FractionalBits"/> fractional bits: the raw <c>ulong</c> value equals the number of
/// whole coins multiplied by <see cref="One"/>. The API moves these raw values around opaquely
/// (transfers, mints/burns, admin adjustments, transaction amounts all operate on raw units), so
/// no scaling happens in the request path. Only human-authored edges — config files and UIs —
/// convert between whole coins and raw units.
/// </summary>
public static class CoinFixedPoint {
    /// <summary>Number of fractional bits in the fixed-point coin representation.</summary>
    public const int FractionalBits = 24;

    /// <summary>Raw value of exactly one whole coin (<c>2^24 = 16,777,216</c>).</summary>
    public const ulong One = 1UL << FractionalBits;

    /// <summary>
    /// Converts a whole-coin amount to its raw fixed-point representation, saturating at
    /// <see cref="ulong.MaxValue"/> rather than overflowing.
    /// </summary>
    public static ulong FromWholeCoins(ulong wholeCoins)
        => wholeCoins > ulong.MaxValue / One ? ulong.MaxValue : wholeCoins * One;
}
