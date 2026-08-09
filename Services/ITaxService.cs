using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

/// <summary>The configured tax target for an official app.</summary>
public class OfficialAppTaxTarget {
    public string AppId { get; init; } = "";
    public ulong TargetBalance { get; init; }
}

/// <summary>
/// The schedule and rate ceiling of the tax, derived entirely from configuration and the schedule
/// anchor. Everything here is knowable in advance and applies uniformly to every taxable balance,
/// which is what makes it safe to publish: it lets a coin holder — or an app holding coins for its
/// users — price the cost of holding before committing to it.
/// <para>
/// Deliberately excludes the rate a run would charge <i>right now</i>. In dynamic mode that figure
/// is derived from aggregates over every balance in the economy, so serving it would both expose
/// total wealth and put a full-table scan behind an unauthenticated route.
/// <see cref="MaxRatePercent"/> is the decision-relevant number anyway — the realised rate is
/// never above it.
/// </para>
/// </summary>
public class TaxScheduleInfo {
    /// <summary>True when periodic collection is actually armed (a period is set and BOSS is configured).</summary>
    public bool Scheduled { get; init; }

    /// <summary>The configured cycle period in hours, or 0 when periodic collection is off.</summary>
    public ulong PeriodHours { get; init; }

    /// <summary>True when the rate is recomputed each cycle from official-app deficits instead of being fixed.</summary>
    public bool DynamicRate { get; init; }

    /// <summary>
    /// The most a single cycle can take from a balance, as a decimal percentage string. This is the
    /// hard ceiling: dynamic mode is clamped to it, and the only other adjustment a run makes
    /// (capping the rate so collection cannot overflow the BOSS balance) can lower the rate but
    /// never raise it.
    /// </summary>
    public string MaxRatePercent { get; init; } = "0";

    /// <summary>The configured fixed rate, which is what gets charged when <see cref="DynamicRate"/> is false.</summary>
    public string FixedRatePercent { get; init; } = "0";

    /// <summary>The ceiling dynamic mode is clamped to. Only meaningful when <see cref="DynamicRate"/> is true.</summary>
    public string MaxDynamicRatePercent { get; init; } = "0";

    /// <summary>
    /// When the next cycle boundary falls, or null when there is none to report — either nothing is
    /// scheduled, or the configured period is so long the boundary is not a representable date. A
    /// value in the past means a cycle is already due and settles on the next scheduler pass.
    /// </summary>
    public DateTime? NextCycleUtc { get; init; }

    /// <summary>
    /// The most cycles that can be charged in one catch-up burst after an outage. Boundaries older
    /// than this are written off as skipped rather than collected, so this bounds the worst case —
    /// but it also means elapsed wall-clock time is not a reliable count of cycles charged.
    /// </summary>
    public int MaxCatchUpCycles { get; init; }
}

public class TaxPreview {
    public bool CanRun { get; init; }
    public string? BlockedReason { get; init; }
    public bool DynamicRate { get; init; }
    public string BossAppId { get; init; } = "";
    public string Rate { get; init; } = "0";
    public string FixedRate { get; init; } = "0";
    public string MaxDynamicRate { get; init; } = "0";

    /// <summary>
    /// Number of balances charged a non-zero amount. For a preview this is the exact count that
    /// would be charged at the computed rate, not an estimate.
    /// </summary>
    public int UsersTaxed { get; init; }
    public ulong Collected { get; init; }
    public int AppsNeedingFunds { get; init; }
    public ulong Distributed { get; init; }
    public ulong BossStartingBalance { get; init; }
    public ulong BossEndingBalance { get; init; }
}

public class TaxRunResult : TaxPreview {
    /// <summary>Id of the recorded cycle, or null when no cycle was claimed (e.g. a run already in progress).</summary>
    public long? CycleId { get; init; }
}

/// <summary>A recorded tax run, for history and reporting.</summary>
public class TaxCycleRecord {
    public long Id { get; init; }
    public DateTime? ScheduledForUtc { get; init; }
    public bool Manual { get; init; }
    public TaxCycleStatus Status { get; init; }
    public TaxCyclePhase Phase { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool DynamicRate { get; init; }
    public string Rate { get; init; } = "0";
    public string BossAppId { get; init; } = "";
    public ulong Collected { get; init; }
    public int AccountsTaxed { get; init; }
    public ulong Distributed { get; init; }
    public int AppsPaid { get; init; }
    public int AppsNeedingFunds { get; init; }
    public ulong BossStartingBalance { get; init; }
    public ulong BossEndingBalance { get; init; }

    /// <summary>Failed execution attempts. Non-zero means the run was interrupted and retried.</summary>
    public int Attempts { get; init; }

    /// <summary>Why the cycle did not run, or the error from the last failed attempt.</summary>
    public string? BlockedReason { get; init; }
}

public class TaxCyclePage {
    public List<TaxCycleRecord> Cycles { get; init; } = [];
    public long TotalCount { get; init; }
}

public interface ITaxService {
    Task<OfficialAppTaxTarget> GetOfficialAppTarget(string appId, CancellationToken cancellationToken = default);
    Task SetOfficialAppTarget(string appId, ulong targetBalance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the published tax schedule and rate ceiling. Reads configuration and the schedule
    /// anchor only — it never touches the balance table, so it is cheap enough to serve on an
    /// unauthenticated route.
    /// </summary>
    Task<TaxScheduleInfo> GetScheduleInfo(CancellationToken cancellationToken = default);

    /// <summary>Computes what a run would do right now. Never mutates any state.</summary>
    Task<TaxPreview> PreviewTaxRun(CancellationToken cancellationToken = default);

    /// <summary>Runs one tax cycle immediately, outside the schedule. Recorded as a manual cycle.</summary>
    Task<TaxRunResult> RunTaxNow(CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the schedule by at most one cycle per call. The background service ticks
    /// frequently enough to work through a backlog over successive passes, which keeps any single
    /// run's transaction footprint bounded.
    /// </summary>
    Task RunDueTaxCycles(CancellationToken cancellationToken = default);

    Task<TaxCyclePage> GetTaxCycles(int skip, int take, CancellationToken cancellationToken = default);
    Task<TaxCycleRecord?> GetTaxCycle(long id, CancellationToken cancellationToken = default);
}
