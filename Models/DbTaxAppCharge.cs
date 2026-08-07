using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// What one tax cycle took from one app-owned balance.
/// <para>
/// Collection is chunked and each chunk commits separately, so an in-memory per-app tally would be
/// lost by any interruption — and the events emitted at the end of the run would then under-report.
/// Writing the tally as rows in the charging transaction makes it survive a resume for the same
/// reason the cursor does.
/// </para>
/// <para>
/// Insert-only: the cursor walks each balance exactly once per cycle, so a row per
/// <c>(CycleId, BalanceId)</c> never needs a read-modify-write, and the unique index turns any
/// double-charge bug into a loud constraint violation. Only app-owned balances are recorded — user
/// balances are the bulk of the table and no one is notified about them.
/// </para>
/// </summary>
[Index(nameof(CycleId), nameof(BalanceId), IsUnique = true)]
[Index(nameof(CycleId), nameof(AppId))]
public class DbTaxAppCharge {
    [Key]
    public long Id { get; set; }

    [ForeignKey(nameof(CycleNavigation))]
    public long CycleId { get; set; }

    [StringLength(64)]
    public string AppId { get; set; } = null!;

    [StringLength(64)]
    public string BalanceId { get; set; } = null!;

    public ulong Amount { get; set; }

    public ulong BalanceBefore { get; set; }

    public ulong BalanceAfter { get; set; }

    // navigation properties
    public DbTaxCycle CycleNavigation { get; set; } = null!;
}
