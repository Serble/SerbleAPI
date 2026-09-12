using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// A coin balance owned by some entity (user or app). Each balance has its own id so a
/// single entity can own multiple balances in the future. The (OwnerType, OwnerId) pair is
/// <b>uniquely</b> indexed: readers resolve an owner's default balance as the oldest row, so a
/// second row for the same owner is a place credits can land and never be seen again. The index is
/// what makes the create-on-demand path safe — a losing concurrent insert is rejected by the
/// database instead of quietly producing a duplicate.
///
/// <para><b>Mutation.</b> <see cref="Coins"/> has no public setter and every change goes through
/// <see cref="Credit"/>, <see cref="TryDebit"/> or <see cref="SetCoins"/>, each of which bumps
/// <see cref="RowVersion"/>. That is not bookkeeping: <see cref="RowVersion"/> is a
/// <see cref="ConcurrencyCheckAttribute"/> token, so EF writes it into the <c>WHERE</c> clause of
/// every update and a write built on a stale read affects no rows and throws
/// <see cref="DbUpdateConcurrencyException"/> rather than overwriting someone else's coins with an
/// absolute value. Callers are still expected to hold the row lock
/// (<c>SELECT … FOR UPDATE</c>) — the token is the second layer that turns a forgotten lock into a
/// loud failure instead of a silent mint.</para>
/// </summary>
[Index(nameof(OwnerType), nameof(OwnerId), IsUnique = true)]
public class DbBalance {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    public int OwnerType { get; set; }

    [StringLength(64)]
    public string OwnerId { get; set; } = null!;

    /// <summary>
    /// The balance. Written only through <see cref="Credit"/>, <see cref="TryDebit"/> and
    /// <see cref="SetCoins"/> so that no change can skip the concurrency token; EF materialises it
    /// through the private setter.
    /// </summary>
    public ulong Coins { get; private set; }

    /// <summary>
    /// Optimistic-concurrency token, bumped by every mutation of <see cref="Coins"/>. MySQL has no
    /// native rowversion, so the value is maintained here rather than by the database.
    /// </summary>
    [ConcurrencyCheck]
    public ulong RowVersion { get; private set; }

    public DateTime DateCreated { get; set; }

    /// <summary>Adds <paramref name="amount"/>, saturating at <see cref="ulong.MaxValue"/>.</summary>
    public void Credit(ulong amount) =>
        SetCoins(amount > ulong.MaxValue - Coins ? ulong.MaxValue : Coins + amount);

    /// <summary>
    /// Subtracts <paramref name="amount"/> and returns true, or leaves the balance untouched and
    /// returns false when it would go negative. Checking and subtracting in one call is what keeps
    /// a caller from validating affordability and then debiting against a different value.
    /// </summary>
    public bool TryDebit(ulong amount) {
        if (Coins < amount) return false;
        SetCoins(Coins - amount);
        return true;
    }

    /// <summary>
    /// Subtracts <paramref name="amount"/>, clamping at zero, and returns how much was actually
    /// taken. For the paths whose contract is "take what is there".
    /// </summary>
    public ulong DebitUpTo(ulong amount) {
        ulong taken = Math.Min(amount, Coins);
        SetCoins(Coins - taken);
        return taken;
    }

    /// <summary>Replaces the balance with an absolute value (administrative adjustment).</summary>
    public void SetCoins(ulong value) {
        if (value == Coins) return;
        Coins = value;
        RowVersion++;
    }

    /// <summary>Whether <paramref name="amount"/> can be added without saturating.</summary>
    public bool CanCredit(ulong amount) => Coins <= ulong.MaxValue - amount;
}
