namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Lifecycle state of a recorded tax run. Stored as an int in the <c>TaxCycles</c> table so
/// additional states can be appended without breaking existing rows.
/// </summary>
public enum TaxCycleStatus {
    /// <summary>Claimed and in progress (or interrupted part-way and awaiting resume).</summary>
    Running = 0,

    /// <summary>Collection and distribution both finished.</summary>
    Completed = 1,

    /// <summary>Nothing was collected or distributed; see <c>BlockedReason</c>. No balances changed.</summary>
    Blocked = 2,

    /// <summary>The run threw before reaching a terminal state. Committed chunks are kept.</summary>
    Failed = 3,

    /// <summary>
    /// The cycle boundary elapsed while the server was down and fell outside the catch-up
    /// window, so it was recorded but never executed.
    /// </summary>
    Skipped = 4
}

/// <summary>
/// How far through a tax run the executor has progressed. Collection is chunked and resumable,
/// so an interrupted run resumes from its recorded phase and cursor rather than restarting.
/// </summary>
public enum TaxCyclePhase {
    Collecting = 0,
    Distributing = 1,
    Finished = 2
}
