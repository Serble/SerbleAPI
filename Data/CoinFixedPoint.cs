using System.Globalization;

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

    /// <summary>
    /// Parses a human-authored <b>decimal</b> coin amount (e.g. <c>"0.5"</c>) into raw fixed-point
    /// units, rounding to the nearest representable unit. Returns false (with an error message) for
    /// non-numbers, negatives, or amounts that overflow the raw range.
    /// </summary>
    public static bool TryParseCoins(string? input, out ulong raw, out string? error) {
        raw = 0;
        error = null;
        if (!decimal.TryParse((input ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal coins)) {
            error = "Must be a number.";
            return false;
        }
        if (coins < 0) {
            error = "Must be zero or positive.";
            return false;
        }
        decimal rawDec = Math.Round(coins * One, MidpointRounding.AwayFromZero);
        if (rawDec > ulong.MaxValue) {
            error = "Amount is too large.";
            return false;
        }
        raw = (ulong)rawDec;
        return true;
    }

    /// <summary>
    /// Formats raw fixed-point units as a decimal whole-coin string, trimmed to the precision the
    /// representation can carry (8 fractional digits) with no trailing zeros.
    /// </summary>
    public static string ToCoinsString(ulong raw)
        => ((decimal)raw / One).ToString("0.########", CultureInfo.InvariantCulture);
}
