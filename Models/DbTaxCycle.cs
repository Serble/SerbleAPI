using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// A permanent record of one tax run. Every run — scheduled, manual, blocked or skipped — gets a
/// row, so the table doubles as the economy's tax history.
/// <para>
/// It is also the concurrency primitive. <see cref="ScheduledForUtc"/> carries a unique index, so
/// claiming a cycle boundary is an INSERT that at most one instance can win. That makes scheduled
/// collection idempotent across replicas even if the advisory run lock is unavailable: a given
/// boundary can never be taxed twice.
/// </para>
/// <para>
/// Collection is chunked and committed incrementally, so <see cref="Phase"/> and
/// <see cref="CursorBalanceId"/> record how far a run got. An interrupted run resumes from the
/// cursor instead of re-taxing accounts it already charged.
/// </para>
/// </summary>
[Index(nameof(ScheduledForUtc), IsUnique = true)]
[Index(nameof(Status))]
[Index(nameof(StartedAt))]
public class DbTaxCycle {
    /// <summary>Sequential run number. Referenced by ledger entries as "cycle #N".</summary>
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// The cycle boundary this run settles, or null for an admin-triggered run. Unique, so a
    /// boundary can only ever be claimed once. Manual runs are all null — MySQL permits repeated
    /// NULLs in a unique index — and are instead serialised by the advisory run lock.
    /// </summary>
    public DateTime? ScheduledForUtc { get; set; }

    /// <summary>
    /// True when triggered by an admin rather than the scheduler. Named <c>IsManual</c> rather than
    /// <c>Manual</c> because MANUAL is a reserved word in MySQL 8.0.31+.
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>Maps to <see cref="Data.Schemas.TaxCycleStatus"/>.</summary>
    public int Status { get; set; }

    /// <summary>Maps to <see cref="Data.Schemas.TaxCyclePhase"/>.</summary>
    public int Phase { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Failed execution attempts. A run that throws part-way stays <see cref="Status"/> Running so
    /// the next pass resumes it from the cursor; this counter is what stops a permanently broken
    /// cycle from retrying forever and blocking the schedule behind it.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>Instance that last worked this cycle. Diagnostic only; takeover is gated on the run lock.</summary>
    [StringLength(128)]
    public string? LeaseOwner { get; set; }

    /// <summary>When the owning instance last reported progress. Diagnostic only.</summary>
    public DateTime? LeaseRenewedUtc { get; set; }

    public bool DynamicRate { get; set; }

    /// <summary>
    /// The rate actually applied, as a percentage. Frozen at plan time and reused on resume so a
    /// restart mid-run cannot charge accounts at a different rate than their peers.
    /// </summary>
    [Column(TypeName = "decimal(20,10)")]
    public decimal RatePercent { get; set; }

    [StringLength(64)]
    public string BossAppId { get; set; } = "";

    public ulong BossStartingBalance { get; set; }

    public ulong BossEndingBalance { get; set; }

    /// <summary>Total coins collected across every chunk.</summary>
    public ulong Collected { get; set; }

    /// <summary>Number of balances actually charged a non-zero amount.</summary>
    public int AccountsTaxed { get; set; }

    /// <summary>Total coins paid out to official apps.</summary>
    public ulong Distributed { get; set; }

    public int AppsPaid { get; set; }

    /// <summary>Official apps below their target balance when the run was planned.</summary>
    public int AppsNeedingFunds { get; set; }

    /// <summary>
    /// Highest balance id already collected from. Collection walks the clustered primary key in
    /// ascending order, so this is a complete resume point. Empty means nothing processed yet.
    /// </summary>
    [StringLength(64)]
    public string CursorBalanceId { get; set; } = "";

    [StringLength(512)]
    public string? BlockedReason { get; set; }
}
