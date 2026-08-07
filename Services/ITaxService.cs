using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

/// <summary>The configured tax target for an official app.</summary>
public class OfficialAppTaxTarget {
    public string AppId { get; init; } = "";
    public ulong TargetBalance { get; init; }
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
