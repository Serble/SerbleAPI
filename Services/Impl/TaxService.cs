using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// Periodic wealth tax: coins are collected from every balance into the BOSS app's balance, then
/// redistributed to official apps that sit below their configured target. Official apps are taxed
/// alongside everyone else — being a recipient of the redistribution does not exempt an app from
/// funding it — so the only account collection skips is BOSS itself.
///
/// <para><b>Scaling model.</b> A run never loads the balance table into memory. The rate is
/// computed from SQL aggregates, then collection walks the clustered primary key in chunks of
/// <see cref="CollectionChunkSize"/>, each in its own short READ COMMITTED transaction. Lock hold
/// time is therefore bounded by one chunk rather than by the size of the economy, so ordinary
/// economy traffic keeps flowing while tax runs.</para>
///
/// <para><b>The trade-off.</b> Chunked commits mean a run is no longer atomic — a crash leaves the
/// population part-taxed. That is recovered rather than prevented: each chunk commits its progress
/// cursor onto the <see cref="DbTaxCycle"/> row, and the next pass resumes from there instead of
/// restarting, so no account is charged twice and none is skipped.</para>
///
/// <para><b>Concurrency.</b> Two layers. <see cref="DbAdvisoryLock"/> gives coarse server-wide mutual
/// exclusion so replicas do not collect concurrently; the unique index on
/// <see cref="DbTaxCycle.ScheduledForUtc"/> is the hard guarantee that a given cycle boundary is
/// settled at most once, and holds even if the advisory lock is unavailable.</para>
///
/// <para><b>Notifications.</b> Apps that subscribe are told what tax did to their balance, but only
/// through the outbox: rows are written in the transaction that finalises the cycle and delivered
/// later by <see cref="WebhookDispatcherService"/>. Nothing in this class ever makes an HTTP call —
/// collection holds row locks, and a hanging app endpoint must not be able to hold them with it.</para>
/// </summary>
public class TaxService(SerbleDbContext db, ILogger<TaxService> logger) : ITaxService {
    private const string TaxRunLockName = "serble.economy.tax.run";
    private const string LastRunKey = "economy.tax._last_run_utc";
    private const string AppTargetPrefix = "economy.tax.target_balance.";

    /// <summary>
    /// How far behind the schedule may fall before the gap is written off. Cycles older than this
    /// are recorded as <see cref="TaxCycleStatus.Skipped"/> rather than executed, so a server that
    /// was down for a month does not spend hours retroactively taxing everyone.
    /// </summary>
    private const int MaxCatchUpCycles = 24;

    /// <summary>
    /// Balances charged per transaction. Trades lock contention against round trips: bigger chunks
    /// mean fewer commits but longer row-lock holds against live economy traffic.
    /// </summary>
    private const int CollectionChunkSize = 500;

    /// <summary>
    /// Failed attempts before a cycle is abandoned. Interrupted runs are retried rather than
    /// written off — a half-collected cycle has coins sitting in BOSS and a part-taxed population,
    /// so finishing it matters more than failing fast — but a cycle that cannot make progress must
    /// eventually give up or it blocks every later cycle behind it.
    /// </summary>
    private const int MaxCycleAttempts = 5;

    private static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    private sealed class TaxSettings {
        public required TimeSpan Period { get; init; }
        public required decimal FixedRatePercent { get; init; }
        public required bool UseDynamicRate { get; init; }
        public required decimal MaxDynamicRatePercent { get; init; }
        public required string BossAppId { get; init; }
        public bool SchedulerEnabled => Period > TimeSpan.Zero && !string.IsNullOrWhiteSpace(BossAppId);
        public bool ManualRunEnabled => !string.IsNullOrWhiteSpace(BossAppId);
    }

    private sealed class OfficialRecipient {
        public required string AppId { get; init; }
        public required ulong TargetBalance { get; init; }
        public required ulong CurrentBalance { get; init; }
        public ulong Deficit => TargetBalance > CurrentBalance ? TargetBalance - CurrentBalance : 0;
    }

    /// <summary>
    /// What a run would do, computed entirely from aggregates. Producing a plan never writes
    /// anything, which is what lets preview and the blocked-reason check share this code path
    /// without risking a mutation.
    /// </summary>
    private sealed class TaxPlan {
        public required TaxSettings Settings { get; init; }
        public required string? BlockedReason { get; init; }
        public bool CanRun => BlockedReason == null;
        public required ulong BossStartingBalance { get; init; }
        public required decimal RatePercent { get; init; }
        public required ulong ExpectedCollection { get; init; }
        public required int ExpectedAccountsTaxed { get; init; }
        public required ulong ExpectedDistribution { get; init; }
        public required ulong ExpectedBossEnding { get; init; }
        public required int AppsNeedingFunds { get; init; }
    }

    // ---------------------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------------------

    public async Task<OfficialAppTaxTarget> GetOfficialAppTarget(string appId, CancellationToken cancellationToken = default) {
        ulong target = await GetAppTarget(appId, cancellationToken);
        return new OfficialAppTaxTarget { AppId = appId, TargetBalance = target };
    }

    public async Task<TaxScheduleInfo> GetScheduleInfo(CancellationToken cancellationToken = default) {
        TaxSettings settings = await LoadSettings(cancellationToken);

        // Whichever mode is active decides the ceiling: dynamic runs are clamped to the max
        // dynamic rate, fixed runs charge the fixed rate exactly.
        decimal maxRate = ClampPercent(settings.UseDynamicRate
            ? settings.MaxDynamicRatePercent
            : settings.FixedRatePercent);

        DateTime? next = null;
        if (settings.SchedulerEnabled) {
            DateTime? anchor = await GetAnchor(cancellationToken);
            // With no anchor stored yet the scheduler stamps one at the current time on its next
            // pass, so the first boundary is a full period out rather than immediately due.
            DateTime basis = anchor ?? DateTime.UtcNow;
            // The period is only validated as a non-negative integer, so a large enough one puts
            // the boundary past DateTime.MaxValue. Report "no computable boundary" rather than
            // letting an unauthenticated read throw on a misconfiguration.
            if (settings.Period <= DateTime.MaxValue - basis) next = basis + settings.Period;
        }

        return new TaxScheduleInfo {
            Scheduled             = settings.SchedulerEnabled,
            PeriodHours           = (ulong)settings.Period.TotalHours,
            DynamicRate           = settings.UseDynamicRate,
            MaxRatePercent        = PercentToString(maxRate),
            FixedRatePercent      = PercentToString(ClampPercent(settings.FixedRatePercent)),
            MaxDynamicRatePercent = PercentToString(ClampPercent(settings.MaxDynamicRatePercent)),
            NextCycleUtc          = next,
            MaxCatchUpCycles      = MaxCatchUpCycles
        };
    }

    public async Task SetOfficialAppTarget(string appId, ulong targetBalance, CancellationToken cancellationToken = default) {
        await UpsertKv(AppTargetKey(appId), targetBalance.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaxPreview> PreviewTaxRun(CancellationToken cancellationToken = default) {
        TaxSettings settings = await LoadSettings(cancellationToken);
        if (!settings.ManualRunEnabled) {
            return BlockedPreview(settings, "BOSS app id is not configured.");
        }
        TaxPlan plan = await BuildPlan(settings, cancellationToken);
        return ToPreview(plan);
    }

    public async Task<TaxRunResult> RunTaxNow(CancellationToken cancellationToken = default) {
        TaxSettings settings = await LoadSettings(cancellationToken);
        if (!settings.ManualRunEnabled) {
            return ToRunResult(BlockedPreview(settings, "BOSS app id is not configured."), null);
        }

        await using DbAdvisoryLock runLock = await DbAdvisoryLock.TryAcquire(db, TaxRunLockName, cancellationToken);
        if (!runLock.Acquired) {
            return ToRunResult(BlockedPreview(settings, "A tax run is already in progress."), null);
        }

        // Refuse to start new work on top of an unfinished run — the scheduler resumes those, and
        // starting a second collection over a half-taxed population would double-charge whichever
        // accounts the interrupted run had not reached.
        DbTaxCycle? unfinished = await FindResumableCycle(cancellationToken);
        if (unfinished != null) {
            return ToRunResult(
                BlockedPreview(settings, $"Tax cycle #{unfinished.Id} is unfinished and must be resumed before a new run can start."),
                unfinished.Id);
        }

        TaxPlan plan = await BuildPlan(settings, cancellationToken);
        DbTaxCycle? cycle = await ClaimCycle(plan, scheduledFor: null, manual: true, cancellationToken);
        if (cycle == null) {
            return ToRunResult(BlockedPreview(settings, "A tax run is already in progress."), null);
        }

        if ((TaxCycleStatus)cycle.Status == TaxCycleStatus.Blocked) {
            return ToRunResult(ToPreview(plan), cycle.Id);
        }

        await ExecuteCycle(cycle.Id, cancellationToken);
        return await BuildResultFromCycle(cycle.Id, plan.Settings, cancellationToken);
    }

    public async Task RunDueTaxCycles(CancellationToken cancellationToken = default) {
        await using DbAdvisoryLock runLock = await DbAdvisoryLock.TryAcquire(db, TaxRunLockName, cancellationToken);
        if (!runLock.Acquired) return;

        // Finishing an interrupted run always takes priority, and happens even when the scheduler
        // has since been disabled — otherwise a run stopped mid-collection would leave the
        // population permanently part-taxed.
        DbTaxCycle? resumable = await FindResumableCycle(cancellationToken);
        if (resumable != null) {
            logger.LogInformation("Resuming interrupted tax cycle #{CycleId} from phase {Phase}",
                resumable.Id, (TaxCyclePhase)resumable.Phase);
            try {
                await ExecuteCycle(resumable.Id, cancellationToken);
            }
            finally {
                // The resumed boundary must stop being "due" once it settles, or the next pass
                // would try to claim it again and be rejected by the unique index forever.
                await AdvanceAnchorPastSettledCycle(resumable.Id, cancellationToken);
            }
            return;
        }

        TaxSettings settings = await LoadSettings(cancellationToken);
        DateTime now = DateTime.UtcNow;

        if (!settings.SchedulerEnabled) {
            await SetAnchor(now, cancellationToken);
            return;
        }

        DateTime? anchor = await GetAnchor(cancellationToken);
        if (anchor == null) {
            await SetAnchor(now, cancellationToken);
            return;
        }

        if (now < anchor.Value + settings.Period) return;

        long behind = (now - anchor.Value).Ticks / settings.Period.Ticks;
        if (behind > MaxCatchUpCycles) {
            long skipped = behind - MaxCatchUpCycles;
            DateTime skipTo = anchor.Value.AddTicks(settings.Period.Ticks * skipped);
            await RecordSkippedGap(settings, skipTo, skipped, cancellationToken);
            anchor = skipTo;
            await SetAnchor(skipTo, cancellationToken);
            logger.LogWarning("Tax schedule was {Skipped} cycle(s) beyond the catch-up window; those cycles were recorded as skipped", skipped);
        }

        // Exactly one cycle per pass. The background service ticks every minute, so a backlog
        // drains over successive passes instead of collapsing into one enormous transaction.
        DateTime boundary = anchor.Value + settings.Period;
        TaxPlan plan = await BuildPlan(settings, cancellationToken);
        DbTaxCycle? cycle = await ClaimCycle(plan, boundary, manual: false, cancellationToken);
        if (cycle == null) {
            // Another replica claimed this boundary, or it was already recorded. Either way it is
            // settled, so step over it — but only once a row is confirmed to exist, so a transient
            // write failure cannot silently skip a cycle.
            bool alreadyRecorded = await db.TaxCycles.AsNoTracking()
                .AnyAsync(c => c.ScheduledForUtc == boundary, cancellationToken);
            if (alreadyRecorded) await SetAnchor(boundary, cancellationToken);
            return;
        }

        if ((TaxCycleStatus)cycle.Status == TaxCycleStatus.Running) {
            await ExecuteCycle(cycle.Id, cancellationToken);
        }
        else {
            logger.LogInformation("Tax cycle #{CycleId} for {Boundary:o} recorded as blocked: {Reason}",
                cycle.Id, boundary, cycle.BlockedReason);
        }

        // The boundary is settled either way. A blocked cycle advances the schedule because it
        // leaves a row explaining itself, rather than being silently dropped.
        await SetAnchor(boundary, cancellationToken);
    }

    public async Task<TaxCyclePage> GetTaxCycles(int skip, int take, CancellationToken cancellationToken = default) {
        long total = await db.TaxCycles.AsNoTracking().LongCountAsync(cancellationToken);
        List<DbTaxCycle> rows = await db.TaxCycles.AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new TaxCyclePage { Cycles = rows.Select(ToRecord).ToList(), TotalCount = total };
    }

    public async Task<TaxCycleRecord?> GetTaxCycle(long id, CancellationToken cancellationToken = default) {
        DbTaxCycle? row = await db.TaxCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return row == null ? null : ToRecord(row);
    }

    // ---------------------------------------------------------------------------------------
    // Planning (read-only)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Works out the rate and expected outcome from SQL aggregates alone — three scalar queries
    /// regardless of how many balances exist. Guaranteed not to write anything.
    /// </summary>
    private async Task<TaxPlan> BuildPlan(TaxSettings settings, CancellationToken cancellationToken) {
        DbApp? bossApp = await db.Apps.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == settings.BossAppId, cancellationToken);
        if (bossApp == null) {
            return EmptyPlan(settings, $"Configured BOSS app '{settings.BossAppId}' does not exist.");
        }

        ulong bossStartingBalance = await ReadDefaultBalanceCoins(BalanceOwnerType.App, settings.BossAppId, cancellationToken);
        ulong bossCapacity = ulong.MaxValue - bossStartingBalance;

        string[] exempt = GetExemptAppIds(settings.BossAppId);
        List<OfficialRecipient> recipients = await LoadRecipients(settings.BossAppId, cancellationToken);

        decimal totalWealth = await SumTaxableCoins(exempt, cancellationToken);
        long taxableCount = await CountTaxableBalances(exempt, cancellationToken);

        decimal ratePercent = settings.UseDynamicRate
            ? ComputeDynamicRatePercent(totalWealth, taxableCount, recipients, bossStartingBalance, settings.MaxDynamicRatePercent)
            : ClampPercent(settings.FixedRatePercent);
        ratePercent = CapRateToBossCapacity(ratePercent, totalWealth, taxableCount, bossCapacity);

        ulong expectedCollection = 0;
        int expectedAccountsTaxed = 0;
        if (ratePercent > 0) {
            expectedCollection = ClampToUlong(await SumTaxDue(exempt, ratePercent, cancellationToken), bossCapacity);
            expectedAccountsTaxed = (int)Math.Min(await CountTaxDuePayers(exempt, ratePercent, cancellationToken), int.MaxValue);
        }

        ulong bossAfterCollection = bossStartingBalance + expectedCollection;

        // Distribution runs after collection, so it is the taxed recipients it has to fill.
        List<OfficialRecipient> projected = ProjectAfterCollection(recipients, ratePercent);
        int appsNeedingFunds = projected.Count(r => r.Deficit > 0);
        ulong expectedDistribution = PreviewDistribution(bossAfterCollection, projected);

        string? blockedReason = DetermineBlockedReason(
            settings, appsNeedingFunds, totalWealth, bossCapacity, ratePercent, expectedCollection, expectedDistribution);

        return new TaxPlan {
            Settings = settings,
            BlockedReason = blockedReason,
            BossStartingBalance = bossStartingBalance,
            RatePercent = ratePercent,
            ExpectedCollection = expectedCollection,
            ExpectedAccountsTaxed = expectedAccountsTaxed,
            ExpectedDistribution = expectedDistribution,
            ExpectedBossEnding = bossAfterCollection - expectedDistribution,
            AppsNeedingFunds = appsNeedingFunds
        };
    }

    private static string? DetermineBlockedReason(
        TaxSettings settings,
        int appsNeedingFunds,
        decimal totalTaxableWealth,
        ulong bossCapacity,
        decimal ratePercent,
        ulong expectedCollection,
        ulong expectedDistribution) {

        if (bossCapacity == 0) {
            return "The BOSS account balance is already at the maximum value, so it cannot receive any more tax.";
        }
        if (expectedCollection > 0 || expectedDistribution > 0) return null;

        bool anyCoinsToTax = totalTaxableWealth > 0;

        if (settings.UseDynamicRate) {
            if (settings.MaxDynamicRatePercent == 0) {
                return "Dynamic tax is enabled, but the max dynamic tax rate setting is 0%.";
            }
            if (appsNeedingFunds == 0) {
                return "No official apps currently need funds because all configured target balances are already met.";
            }
            if (!anyCoinsToTax) {
                return "Official apps need funds, but no taxable accounts currently have any coins available to tax.";
            }
            if (ratePercent == 0) {
                return "The dynamic tax calculation resolved to 0% with the current balances.";
            }
            return "With the current balances and settings, a tax run would not collect or distribute any coins after rounding.";
        }

        if (settings.FixedRatePercent == 0) {
            return "Fixed tax mode is enabled, but the fixed tax rate setting is 0%.";
        }
        if (!anyCoinsToTax) {
            return "The fixed tax rate is set, but no taxable accounts currently have any coins available to tax.";
        }
        return "With the current balances and settings, a tax run would not collect or distribute any coins after rounding.";
    }

    /// <summary>
    /// The rate that leaves BOSS holding enough to bring every official app up to its target in this
    /// same cycle's distribution phase.
    ///
    /// <para>The obvious form — <c>deficit / wealth</c> — under-collects, because official apps are
    /// taxed too: charging them widens the very deficits the run exists to close, and an app sitting
    /// exactly on its target is pushed below it. Both sides of the equation therefore move with the
    /// rate, and the run has to out-collect its own damage:
    /// <c>boss + r·wealth = Σ max(0, target − balance·(1 − r))</c>.</para>
    ///
    /// <para>That right-hand side is piecewise linear, not linear — each app joins it at whatever
    /// rate first pushes it under target — so it is solved by iterating rather than in one step:
    /// solve for the apps currently short, see who the resulting rate newly pushes short, solve
    /// again. Each pass either adds an app or reproduces the previous rate, so this settles in at
    /// most one pass per recipient and lands on the <i>smallest</i> rate that covers everyone.</para>
    ///
    /// <para>The rate is then asked for slightly more than the arithmetic needs, because charges are
    /// rounded per balance and enough of them can round down to leave the payout a few coins short
    /// of target — which is exactly the "not quite fulfilled" outcome this is here to avoid. Half a
    /// coin per balance bounds that loss, and asking for it costs nothing over time: an over-collection
    /// lands in BOSS, and BOSS's balance is subtracted from what the next cycle needs to collect.</para>
    /// </summary>
    private static decimal ComputeDynamicRatePercent(
        decimal totalTaxableWealth,
        long taxableBalanceCount,
        IReadOnlyCollection<OfficialRecipient> recipients,
        ulong bossStartBalance,
        decimal maxDynamicRatePercent) {

        if (maxDynamicRatePercent <= 0 || totalTaxableWealth <= 0) return 0;

        decimal roundingAllowance = (taxableBalanceCount + 1) / 2m;
        decimal rate = 0;
        for (int pass = 0; pass <= recipients.Count; pass++) {
            // Σ(target − balance) and Σ(balance) over the apps this rate leaves short. The second is
            // the run's own cost: every coin taken from a recipient comes straight back to it in the
            // payout, so it funds nothing and has to be taxed for twice over.
            decimal shortfall = 0;
            decimal recipientWealth = 0;
            foreach (OfficialRecipient recipient in recipients) {
                ulong afterTax = recipient.CurrentBalance - ComputeDueFromPercent(recipient.CurrentBalance, rate);
                if (recipient.TargetBalance <= afterTax) continue;
                shortfall += (decimal)recipient.TargetBalance - recipient.CurrentBalance;
                recipientWealth += recipient.CurrentBalance;
            }

            decimal needed = shortfall - bossStartBalance;
            // Nobody is left short that BOSS cannot already cover, so this rate is the answer — on
            // the first pass that means no tax at all.
            if (needed <= 0) return ClampPercent(rate);
            needed += roundingAllowance;

            decimal fundableWealth = totalTaxableWealth - recipientWealth;
            // The recipients are the entire taxable economy, so taxing harder only churns their own
            // coins and never closes the gap. Charge the ceiling and let distribution get as close
            // as it can.
            if (fundableWealth <= 0) return ClampPercent(maxDynamicRatePercent);

            decimal nextRate = needed / fundableWealth * 100m;
            if (nextRate >= maxDynamicRatePercent) return ClampPercent(maxDynamicRatePercent);
            // The rate only ever climbs, so this means the last pass added nobody new: converged.
            if (nextRate <= rate) return ClampPercent(rate);
            rate = nextRate;
        }

        return ClampPercent(rate);
    }

    /// <summary>
    /// The recipients as the distribution phase will actually find them — each balance already
    /// reduced by what collection is about to take from it. Planning against their pre-collection
    /// balances would understate what the payout has to cover, and would miss apps that only fall
    /// under target because of this cycle's own tax.
    /// </summary>
    private static List<OfficialRecipient> ProjectAfterCollection(
        IReadOnlyCollection<OfficialRecipient> recipients, decimal ratePercent) =>
        recipients.Select(r => new OfficialRecipient {
            AppId = r.AppId,
            TargetBalance = r.TargetBalance,
            CurrentBalance = r.CurrentBalance - ComputeDueFromPercent(r.CurrentBalance, ratePercent)
        }).ToList();

    /// <summary>
    /// Lowers the rate if collecting it would overflow the BOSS balance. Per-balance rounding can
    /// add at most half a coin each, so the worst case is bounded by
    /// <c>wealth * rate + count / 2</c> — which makes this an O(1) calculation rather than the
    /// repeated whole-table simulation it replaces.
    /// </summary>
    private static decimal CapRateToBossCapacity(decimal ratePercent, decimal totalWealth, long balanceCount, ulong bossCapacity) {
        if (ratePercent <= 0 || totalWealth <= 0) return ratePercent <= 0 ? 0 : ratePercent;

        decimal headroom = bossCapacity - (balanceCount + 1) / 2m;
        if (headroom <= 0) return 0;

        decimal worstCase = totalWealth * (ratePercent / 100m);
        return worstCase <= headroom ? ratePercent : ClampPercent(headroom / totalWealth * 100m);
    }

    private static ulong PreviewDistribution(ulong bossAvailable, IReadOnlyCollection<OfficialRecipient> recipients) {
        Dictionary<string, ulong> payouts = ComputeEvenPayouts(recipients, bossAvailable);
        ulong total = 0;
        foreach (ulong payout in payouts.Values) total += payout;
        return total;
    }

    // ---------------------------------------------------------------------------------------
    // Cycle lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Records the cycle. For scheduled runs the INSERT is the claim: the unique index on
    /// <see cref="DbTaxCycle.ScheduledForUtc"/> means a duplicate key here is another instance
    /// having already taken this boundary, so we return null and do nothing.
    /// </summary>
    private async Task<DbTaxCycle?> ClaimCycle(TaxPlan plan, DateTime? scheduledFor, bool manual, CancellationToken cancellationToken) {
        DbTaxCycle cycle = new() {
            ScheduledForUtc = scheduledFor,
            IsManual = manual,
            Status = (int)(plan.CanRun ? TaxCycleStatus.Running : TaxCycleStatus.Blocked),
            Phase = (int)(plan.CanRun ? TaxCyclePhase.Collecting : TaxCyclePhase.Finished),
            StartedAt = DateTime.UtcNow,
            CompletedAt = plan.CanRun ? null : DateTime.UtcNow,
            LeaseOwner = InstanceId,
            LeaseRenewedUtc = DateTime.UtcNow,
            DynamicRate = plan.Settings.UseDynamicRate,
            RatePercent = plan.RatePercent,
            BossAppId = plan.Settings.BossAppId,
            BossStartingBalance = plan.BossStartingBalance,
            BossEndingBalance = plan.CanRun ? 0 : plan.BossStartingBalance,
            AppsNeedingFunds = plan.AppsNeedingFunds,
            CursorBalanceId = "",
            BlockedReason = Truncate(plan.BlockedReason, 512)
        };

        db.TaxCycles.Add(cycle);
        try {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) {
            logger.LogInformation(ex, "Tax cycle for {Boundary:o} was already claimed by another instance", scheduledFor);
            db.ChangeTracker.Clear();
            return null;
        }

        db.ChangeTracker.Clear();
        return cycle;
    }

    /// <summary>
    /// An unfinished run. Safe to treat as abandoned without checking the lease, because reaching
    /// here means we hold the run lock and therefore nobody else is working it.
    /// </summary>
    private Task<DbTaxCycle?> FindResumableCycle(CancellationToken cancellationToken) {
        int running = (int)TaxCycleStatus.Running;
        return db.TaxCycles.AsNoTracking()
            .Where(c => c.Status == running)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ExecuteCycle(long cycleId, CancellationToken cancellationToken) {
        try {
            // Entering by phase rather than from the start is what makes an interrupted run
            // resumable: collection picks up at the stored cursor, and a run that died after
            // collecting goes straight to distribution.
            TaxCyclePhase phase = await GetCyclePhase(cycleId, cancellationToken);
            while (phase == TaxCyclePhase.Collecting) {
                phase = await CollectChunk(cycleId, cancellationToken);
            }
            if (phase == TaxCyclePhase.Distributing) {
                await Distribute(cycleId, cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            // Shutdown mid-run. Committed chunks stand and the cycle stays Running, so the next
            // pass resumes from the cursor.
            throw;
        }
        catch (Exception ex) {
            await RecordAttemptFailure(cycleId, ex, cancellationToken);
            throw;
        }

        DbTaxCycle? done = await db.TaxCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (done == null) return;

        logger.LogInformation(
            "Tax cycle #{CycleId} complete: mode={Mode}, ratePercent={RatePercent}%, accountsTaxed={AccountsTaxed}, collectedRaw={CollectedRaw} ({CollectedCoins}), appsPaid={AppsPaid}, distributedRaw={DistributedRaw} ({DistributedCoins}), bossEndingBalanceRaw={BossRaw} ({BossCoins})",
            done.Id,
            done.DynamicRate ? "dynamic" : "fixed",
            PercentToString(done.RatePercent),
            done.AccountsTaxed,
            done.Collected,
            CoinFixedPoint.ToCoinsString(done.Collected),
            done.AppsPaid,
            done.Distributed,
            CoinFixedPoint.ToCoinsString(done.Distributed),
            done.BossEndingBalance,
            CoinFixedPoint.ToCoinsString(done.BossEndingBalance));
    }

    /// <summary>
    /// Moves the schedule anchor past a cycle that has reached a terminal state. A cycle still
    /// Running is left alone: it is awaiting another resume attempt and its boundary is not
    /// settled yet.
    /// </summary>
    private async Task AdvanceAnchorPastSettledCycle(long cycleId, CancellationToken cancellationToken) {
        try {
            DbTaxCycle? cycle = await db.TaxCycles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
            if (cycle?.ScheduledForUtc == null) return;
            if ((TaxCycleStatus)cycle.Status == TaxCycleStatus.Running) return;

            DateTime? anchor = await GetAnchor(cancellationToken);
            if (anchor == null || anchor.Value < cycle.ScheduledForUtc.Value) {
                await SetAnchor(cycle.ScheduledForUtc.Value, cancellationToken);
            }
        }
        catch (Exception ex) {
            logger.LogError(ex, "Failed to advance the tax schedule anchor past cycle #{CycleId}", cycleId);
        }
    }

    private async Task<TaxCyclePhase> GetCyclePhase(long cycleId, CancellationToken cancellationToken) {
        int phase = await db.TaxCycles.AsNoTracking()
            .Where(c => c.Id == cycleId)
            .Select(c => c.Phase)
            .FirstOrDefaultAsync(cancellationToken);
        return (TaxCyclePhase)phase;
    }

    /// <summary>
    /// Records a failed attempt. The cycle stays Running — and therefore resumable from its
    /// cursor — until it has burned through <see cref="MaxCycleAttempts"/>, at which point it is
    /// marked Failed so the schedule can move past it. Committed chunks are kept either way; a
    /// Failed cycle leaves a part-taxed population and needs an admin to look at it.
    /// </summary>
    private async Task RecordAttemptFailure(long cycleId, Exception ex, CancellationToken cancellationToken) {
        try {
            db.ChangeTracker.Clear();
            DbTaxCycle? cycle = await db.TaxCycles.FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
            if (cycle == null) return;

            cycle.Attempts++;
            cycle.BlockedReason = Truncate($"Attempt {cycle.Attempts} failed: {ex.Message}", 512);
            if (cycle.Attempts >= MaxCycleAttempts) {
                cycle.Status = (int)TaxCycleStatus.Failed;
                cycle.CompletedAt = DateTime.UtcNow;
                logger.LogError(ex,
                    "Tax cycle #{CycleId} abandoned after {Attempts} failed attempts; it stopped at phase {Phase} with {Collected} coins collected and needs manual review",
                    cycleId, cycle.Attempts, (TaxCyclePhase)cycle.Phase, cycle.Collected);
            }
            else {
                logger.LogWarning(ex, "Tax cycle #{CycleId} attempt {Attempts} failed; it will resume from its cursor on the next pass",
                    cycleId, cycle.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recordEx) {
            logger.LogError(recordEx, "Failed to record the failure of tax cycle #{CycleId}", cycleId);
        }
        finally {
            db.ChangeTracker.Clear();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Collection
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Charges one chunk of balances and commits. Rows are taken with <c>FOR UPDATE</c> so a
    /// concurrent spend cannot be lost between reading a balance and writing the taxed value, and
    /// the cursor advances in the same transaction as the charges so a crash cannot re-charge or
    /// skip anyone.
    /// </summary>
    /// <returns>The phase the cycle is left in — still Collecting if more chunks remain.</returns>
    private async Task<TaxCyclePhase> CollectChunk(long cycleId, CancellationToken cancellationToken) {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction? tx = await BeginReadCommitted(cancellationToken);

        DbTaxCycle cycle = await db.TaxCycles.FirstAsync(c => c.Id == cycleId, cancellationToken);
        string[] exempt = GetExemptAppIds(cycle.BossAppId);

        // BOSS is locked before the chunk, matching the order Distribute uses. Taking the two in a
        // consistent order across both phases removes one class of deadlock against each other and
        // against economy traffic that credits BOSS.
        DbBalance boss = await LoadDefaultBalanceForUpdate(BalanceOwnerType.App, cycle.BossAppId, cancellationToken);
        ulong remainingCapacity = ulong.MaxValue - boss.Coins;

        List<DbBalance> chunk = await LoadChunkForUpdate(cycle.CursorBalanceId, exempt, cancellationToken);
        if (chunk.Count == 0) {
            cycle.Phase = (int)TaxCyclePhase.Distributing;
            cycle.LeaseOwner = InstanceId;
            cycle.LeaseRenewedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return TaxCyclePhase.Distributing;
        }

        ulong chunkTotal = 0;
        int chunkAccounts = 0;
        DateTime createdAt = DateTime.UtcNow;
        string cursor = cycle.CursorBalanceId;

        foreach (DbBalance balance in chunk) {
            cursor = balance.Id;
            if (balance.Id == boss.Id) continue;

            ulong due = ComputeDueFromPercent(balance.Coins, cycle.RatePercent);
            if (due > remainingCapacity) due = remainingCapacity;
            if (due == 0) continue;

            ulong balanceBefore = balance.Coins;
            balance.SetCoins(balance.Coins - due);
            remainingCapacity -= due;
            chunkTotal += due;
            chunkAccounts++;

            // App charges are tallied durably so the notification emitted at the end of the run
            // still reports the full amount after an interrupted cycle resumes. Only apps: user
            // balances are the bulk of the table and nobody is notified about them.
            if (balance.OwnerType == (int)BalanceOwnerType.App) {
                db.TaxAppCharges.Add(new DbTaxAppCharge {
                    CycleId = cycle.Id,
                    AppId = balance.OwnerId,
                    BalanceId = balance.Id,
                    Amount = due,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balance.Coins
                });
            }

            db.Transactions.Add(new DbTransaction {
                Id = Guid.NewGuid().ToString(),
                FromBalanceId = balance.Id,
                ToBalanceId = boss.Id,
                Amount = due,
                Description = $"Periodic tax collection (cycle #{cycle.Id})",
                DateCreated = createdAt
            });
        }

        boss.Credit(chunkTotal);
        cycle.Collected += chunkTotal;
        cycle.AccountsTaxed += chunkAccounts;
        cycle.CursorBalanceId = cursor;
        cycle.LeaseOwner = InstanceId;
        cycle.LeaseRenewedUtc = DateTime.UtcNow;

        // Nothing further can be collected once BOSS is full, so stop walking the table.
        if (remainingCapacity == 0) {
            cycle.Phase = (int)TaxCyclePhase.Distributing;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return (TaxCyclePhase)cycle.Phase;
    }

    private async Task Distribute(long cycleId, CancellationToken cancellationToken) {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction? tx = await BeginReadCommitted(cancellationToken);

        DbTaxCycle cycle = await db.TaxCycles.FirstAsync(c => c.Id == cycleId, cancellationToken);
        DbBalance boss = await LoadDefaultBalanceForUpdate(BalanceOwnerType.App, cycle.BossAppId, cancellationToken);

        List<OfficialRecipient> recipients = await LoadRecipients(cycle.BossAppId, cancellationToken);
        Dictionary<string, ulong> payouts = ComputeEvenPayouts(recipients, boss.Coins);

        ulong totalDistributed = 0;
        int appsPaid = 0;
        DateTime createdAt = DateTime.UtcNow;
        List<AppBalanceChange> paid = [];

        foreach (OfficialRecipient recipient in recipients.OrderBy(r => r.AppId)) {
            if (!payouts.TryGetValue(recipient.AppId, out ulong payout) || payout == 0) continue;

            DbBalance target = await LoadDefaultBalanceForUpdate(BalanceOwnerType.App, recipient.AppId, cancellationToken);
            ulong balanceBefore = target.Coins;
            appsPaid++;
            boss.SetCoins(boss.Coins - payout);
            target.Credit(payout);
            totalDistributed += payout;
            paid.Add(new AppBalanceChange(recipient.AppId, payout, balanceBefore, target.Coins));

            db.Transactions.Add(new DbTransaction {
                Id = Guid.NewGuid().ToString(),
                FromBalanceId = boss.Id,
                ToBalanceId = target.Id,
                Amount = payout,
                Description = $"Periodic tax payout (cycle #{cycle.Id})",
                DateCreated = createdAt
            });
        }

        cycle.Distributed = totalDistributed;
        cycle.AppsPaid = appsPaid;
        cycle.BossEndingBalance = boss.Coins;
        cycle.Phase = (int)TaxCyclePhase.Finished;
        cycle.Status = (int)TaxCycleStatus.Completed;
        cycle.CompletedAt = DateTime.UtcNow;
        cycle.LeaseOwner = InstanceId;
        cycle.LeaseRenewedUtc = DateTime.UtcNow;

        // Notifications are queued here, in this transaction, for two reasons: the cycle is now
        // final so its numbers cannot change under a resume, and an event committed alongside the
        // mutation can never describe a run that later rolled back. Nothing is sent from here —
        // an HTTP call under these row locks would let an app's endpoint stall the economy.
        await EnqueueCycleWebhooks(cycle, paid, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
    }

    /// <summary>What one tax cycle did to one app's balance.</summary>
    private sealed record AppBalanceChange(string AppId, ulong Amount, ulong BalanceBefore, ulong BalanceAfter);

    /// <summary>
    /// Adds one outbox row per subscribed app for the two directions tax moves coins: apps that
    /// were charged (<c>tax.collected</c>) and official apps that were paid (<c>tax.payout</c>).
    /// An official app below its target is on both sides of one cycle — it is taxed like everyone
    /// else and then refilled — so it legitimately receives both events. They stay distinct rows
    /// because the outbox dedupe key is (event type, cycle), not the cycle alone.
    /// <para>
    /// Work is bounded by the number of <i>subscribed</i> apps, not by the number of accounts
    /// taxed: the subscription list is read first and the per-app charge detail is loaded only for
    /// apps on it.
    /// </para>
    /// </summary>
    private async Task EnqueueCycleWebhooks(DbTaxCycle cycle, List<AppBalanceChange> paid, CancellationToken cancellationToken) {
        string[] subscribedAppIds = await db.AppWebhooks.AsNoTracking()
            .Where(w => w.Enabled)
            .Select(w => w.AppId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (subscribedAppIds.Length == 0) return;

        HashSet<string> subscribed = subscribedAppIds.ToHashSet(StringComparer.Ordinal);
        List<WebhookEvent> events = [];

        List<DbTaxAppCharge> charges = await db.TaxAppCharges.AsNoTracking()
            .Where(c => c.CycleId == cycle.Id && subscribedAppIds.Contains(c.AppId))
            .ToListAsync(cancellationToken);

        // An app may own several balances, so the event reports the app's total rather than one
        // row's — otherwise a multi-balance app would see an amount that does not reconcile.
        foreach (IGrouping<string, DbTaxAppCharge> group in charges.GroupBy(c => c.AppId, StringComparer.Ordinal)) {
            ulong amount = 0, before = 0, after = 0;
            foreach (DbTaxAppCharge charge in group) {
                amount += charge.Amount;
                before += charge.BalanceBefore;
                after += charge.BalanceAfter;
            }
            if (amount == 0) continue;
            events.Add(BuildTaxEvent(cycle, WebhookEventTypes.TaxCollected,
                new AppBalanceChange(group.Key, amount, before, after)));
        }

        foreach (AppBalanceChange payout in paid) {
            if (!subscribed.Contains(payout.AppId)) continue;
            events.Add(BuildTaxEvent(cycle, WebhookEventTypes.TaxPayout, payout));
        }

        if (events.Count == 0) return;

        List<DbWebhookDelivery> queued = await WebhookOutbox.Enqueue(db, events, cancellationToken);
        if (queued.Count > 0) {
            logger.LogInformation("Tax cycle #{CycleId} queued {Count} webhook delivery/deliveries across {Apps} app(s)",
                cycle.Id, queued.Count, events.Select(e => e.AppId).Distinct().Count());
        }
    }

    private static WebhookEvent BuildTaxEvent(DbTaxCycle cycle, string eventType, AppBalanceChange change) => new() {
        AppId = change.AppId,
        EventType = eventType,
        CycleId = cycle.Id,
        // A cycle settles at most once, so (event type, cycle) is the natural identity of the
        // occurrence — and what makes a re-entered emit point a no-op rather than a duplicate send.
        DedupeSuffix = cycle.Id.ToString(CultureInfo.InvariantCulture),
        Payload = new WebhookEventPayload {
            CycleId = cycle.Id,
            ScheduledForUtc = cycle.ScheduledForUtc,
            Manual = cycle.IsManual,
            RatePercent = PercentToString(cycle.RatePercent),
            Amount = change.Amount.ToString(CultureInfo.InvariantCulture),
            BalanceBefore = change.BalanceBefore.ToString(CultureInfo.InvariantCulture),
            BalanceAfter = change.BalanceAfter.ToString(CultureInfo.InvariantCulture),
            OccurredAt = cycle.CompletedAt ?? DateTime.UtcNow
        }
    };

    private static ulong ComputeDueFromPercent(ulong balance, decimal ratePercent) {
        if (balance == 0 || ratePercent <= 0) return 0;
        if (ratePercent >= 100) return balance;
        decimal dueDec = Math.Round(balance * (ratePercent / 100m), MidpointRounding.AwayFromZero);
        if (dueDec <= 0) return 0;
        if (dueDec >= balance) return balance;
        return (ulong)dueDec;
    }

    private static Dictionary<string, ulong> ComputeEvenPayouts(IReadOnlyCollection<OfficialRecipient> recipients, ulong available) {
        Dictionary<string, ulong> payouts = recipients.ToDictionary(r => r.AppId, _ => 0UL);
        List<(string AppId, ulong Remaining)> active = recipients
            .Where(r => r.Deficit > 0)
            .OrderBy(r => r.AppId)
            .Select(r => (r.AppId, r.Deficit))
            .ToList();

        while (available > 0 && active.Count > 0) {
            ulong evenShare = available / (ulong)active.Count;
            if (evenShare == 0) {
                for (int i = 0; i < active.Count && available > 0; i++) {
                    (string appId, ulong remaining) = active[i];
                    payouts[appId] += 1;
                    active[i] = (appId, remaining - 1);
                    available--;
                }
            }
            else {
                for (int i = 0; i < active.Count; i++) {
                    (string appId, ulong remaining) = active[i];
                    ulong payout = Math.Min(remaining, evenShare);
                    payouts[appId] += payout;
                    active[i] = (appId, remaining - payout);
                    available -= payout;
                }
            }
            active = active.Where(a => a.Remaining > 0).ToList();
        }

        return payouts;
    }

    // ---------------------------------------------------------------------------------------
    // Data access
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Builds parameterised raw SQL. Balance ids and app ids are interpolated as parameters rather
    /// than literals so the exemption list can never inject.
    /// </summary>
    private sealed class SqlArgs {
        private readonly List<object> _values = [];
        public string Add(object value) {
            _values.Add(value);
            return "{" + (_values.Count - 1) + "}";
        }
        public object[] ToArray() => _values.ToArray();
    }

    /// <summary>
    /// Every balance is taxable except those owned by BOSS, which is the account collecting the tax.
    /// </summary>
    private static string TaxableWhere(SqlArgs args, IReadOnlyList<string> exemptAppIds) {
        if (exemptAppIds.Count == 0) return "1 = 1";
        string list = string.Join(", ", exemptAppIds.Select(id => args.Add(id)));
        return $"NOT (`OwnerType` = {args.Add((int)BalanceOwnerType.App)} AND `OwnerId` IN ({list}))";
    }

    /// <summary>Coins owed at the given rate, mirroring <see cref="ComputeDueFromPercent"/>'s away-from-zero rounding and balance cap.</summary>
    private static string DueExpression(SqlArgs args, decimal ratePercent) =>
        $"LEAST(ROUND(`Coins` * CAST({args.Add(ratePercent / 100m)} AS DECIMAL(30,20))), `Coins`)";

    private async Task<decimal> SumTaxableCoins(IReadOnlyList<string> exempt, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string where = TaxableWhere(args, exempt);
        return await QueryDecimal(
            $"SELECT CAST(COALESCE(SUM(`Coins`), 0) AS DECIMAL(65,0)) AS `Value` FROM `Balances` WHERE {where}",
            args.ToArray(), cancellationToken);
    }

    private async Task<long> CountTaxableBalances(IReadOnlyList<string> exempt, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string where = TaxableWhere(args, exempt);
        return await QueryLong(
            $"SELECT COUNT(*) AS `Value` FROM `Balances` WHERE {where}",
            args.ToArray(), cancellationToken);
    }

    private async Task<decimal> SumTaxDue(IReadOnlyList<string> exempt, decimal ratePercent, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string where = TaxableWhere(args, exempt);
        string due = DueExpression(args, ratePercent);
        return await QueryDecimal(
            $"SELECT CAST(COALESCE(SUM({due}), 0) AS DECIMAL(65,0)) AS `Value` FROM `Balances` WHERE {where}",
            args.ToArray(), cancellationToken);
    }

    private async Task<long> CountTaxDuePayers(IReadOnlyList<string> exempt, decimal ratePercent, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string where = TaxableWhere(args, exempt);
        string due = DueExpression(args, ratePercent);
        return await QueryLong(
            $"SELECT COUNT(*) AS `Value` FROM `Balances` WHERE {where} AND {due} >= 1",
            args.ToArray(), cancellationToken);
    }

    private async Task<List<DbBalance>> LoadChunkForUpdate(string cursor, IReadOnlyList<string> exempt, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string cursorParam = args.Add(cursor);
        string where = TaxableWhere(args, exempt);
        // Ordering by the clustered primary key makes this a forward range scan, so chunk N costs
        // the same as chunk 1 no matter how far into the table it sits.
        string sql =
            $"SELECT * FROM `Balances` WHERE `Id` > {cursorParam} AND {where} " +
            $"ORDER BY `Id` LIMIT {CollectionChunkSize} FOR UPDATE";
        return await db.Balances.FromSqlRaw(sql, args.ToArray()).ToListAsync(cancellationToken);
    }

    private async Task<DbBalance> LoadDefaultBalanceForUpdate(BalanceOwnerType ownerType, string ownerId, CancellationToken cancellationToken) {
        SqlArgs args = new();
        string type = args.Add((int)ownerType);
        string owner = args.Add(ownerId);
        string sql =
            $"SELECT * FROM `Balances` WHERE `OwnerType` = {type} AND `OwnerId` = {owner} " +
            "ORDER BY `DateCreated` LIMIT 1 FOR UPDATE";

        List<DbBalance> rows = await db.Balances.FromSqlRaw(sql, args.ToArray()).ToListAsync(cancellationToken);
        if (rows.Count > 0) return rows[0];

        DbBalance created = new() {
            Id = Guid.NewGuid().ToString(),
            OwnerType = (int)ownerType,
            OwnerId = ownerId,
            DateCreated = DateTime.UtcNow
        };
        db.Balances.Add(created);
        return created;
    }

    /// <summary>Read-only balance lookup for planning. Never creates a row, unlike its FOR UPDATE counterpart.</summary>
    private async Task<ulong> ReadDefaultBalanceCoins(BalanceOwnerType ownerType, string ownerId, CancellationToken cancellationToken) {
        int type = (int)ownerType;
        return await db.Balances.AsNoTracking()
            .Where(b => b.OwnerType == type && b.OwnerId == ownerId)
            .OrderBy(b => b.DateCreated)
            .Select(b => (ulong?)b.Coins)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;
    }

    /// <summary>
    /// Apps whose balances collection skips. Only BOSS: it is the account tax is collected <i>into</i>,
    /// so charging it would move coins from one of its balances to another and inflate the collected
    /// total with coins it already held. Official apps are not exempt — they pay the same rate as
    /// everyone else and get their funding back through the distribution phase.
    /// </summary>
    private static string[] GetExemptAppIds(string bossAppId) =>
        string.IsNullOrWhiteSpace(bossAppId) ? [] : [bossAppId];

    private async Task<List<OfficialRecipient>> LoadRecipients(string bossAppId, CancellationToken cancellationToken) {
        string[] officialAppIds = await db.Apps.AsNoTracking()
            .Where(a => a.IsOfficial && a.Id != bossAppId)
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken);
        if (officialAppIds.Length == 0) return [];

        int appType = (int)BalanceOwnerType.App;
        var balanceRows = await db.Balances.AsNoTracking()
            .Where(b => b.OwnerType == appType && officialAppIds.Contains(b.OwnerId))
            .OrderBy(b => b.OwnerId)
            .ThenBy(b => b.DateCreated)
            .Select(b => new { b.OwnerId, b.Coins })
            .ToListAsync(cancellationToken);

        Dictionary<string, ulong> currentBalances = balanceRows
            .GroupBy(b => b.OwnerId)
            .ToDictionary(g => g.Key, g => g.First().Coins);

        string[] targetKeys = officialAppIds.Select(AppTargetKey).ToArray();
        Dictionary<string, string> targetValues = await db.Kvs.AsNoTracking()
            .Where(k => targetKeys.Contains(k.Key))
            .ToDictionaryAsync(k => k.Key, k => k.Value, cancellationToken);

        List<OfficialRecipient> recipients = new(officialAppIds.Length);
        foreach (string appId in officialAppIds) {
            ulong target = targetValues.TryGetValue(AppTargetKey(appId), out string? raw)
                       && ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                ? parsed
                : 0;
            recipients.Add(new OfficialRecipient {
                AppId = appId,
                TargetBalance = target,
                CurrentBalance = currentBalances.GetValueOrDefault(appId, 0UL)
            });
        }
        return recipients;
    }

    private async Task<decimal> QueryDecimal(string sql, object[] args, CancellationToken cancellationToken) {
        List<decimal?> rows = await db.Database.SqlQueryRaw<decimal?>(sql, args).ToListAsync(cancellationToken);
        return rows.Count == 0 ? 0m : rows[0] ?? 0m;
    }

    private async Task<long> QueryLong(string sql, object[] args, CancellationToken cancellationToken) {
        List<long?> rows = await db.Database.SqlQueryRaw<long?>(sql, args).ToListAsync(cancellationToken);
        return rows.Count == 0 ? 0L : rows[0] ?? 0L;
    }

    private async Task<IDbContextTransaction?> BeginReadCommitted(CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

    // ---------------------------------------------------------------------------------------
    // Schedule anchor + settings
    // ---------------------------------------------------------------------------------------

    private async Task RecordSkippedGap(TaxSettings settings, DateTime lastSkippedBoundary, long skippedCount, CancellationToken cancellationToken) {
        // One row for the whole gap rather than one per boundary — a long outage would otherwise
        // insert thousands of rows describing nothing.
        db.TaxCycles.Add(new DbTaxCycle {
            ScheduledForUtc = lastSkippedBoundary,
            IsManual = false,
            Status = (int)TaxCycleStatus.Skipped,
            Phase = (int)TaxCyclePhase.Finished,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            LeaseOwner = InstanceId,
            DynamicRate = settings.UseDynamicRate,
            RatePercent = 0,
            BossAppId = settings.BossAppId,
            CursorBalanceId = "",
            BlockedReason = Truncate(
                $"{skippedCount} cycle(s) elapsed while the server was not running and fell outside the {MaxCatchUpCycles}-cycle catch-up window.",
                512)
        });
        try {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) {
            logger.LogInformation(ex, "Skipped-gap tax cycle for {Boundary:o} was already recorded", lastSkippedBoundary);
        }
        finally {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<TaxSettings> LoadSettings(CancellationToken cancellationToken) {
        ulong periodHours = await GetIntegerConfig(ServerConfigCatalog.TaxPeriodHours, cancellationToken);
        return new TaxSettings {
            Period = periodHours == 0
                ? TimeSpan.Zero
                : TimeSpan.FromHours(Math.Min(periodHours, (ulong)TimeSpan.MaxValue.TotalHours)),
            FixedRatePercent = await GetPercentConfig(ServerConfigCatalog.TaxFixedRate, cancellationToken),
            UseDynamicRate = await GetBooleanConfig(ServerConfigCatalog.TaxUseDynamicRate, cancellationToken),
            MaxDynamicRatePercent = await GetPercentConfig(ServerConfigCatalog.TaxMaxDynamicRate, cancellationToken),
            BossAppId = await GetStringConfig(ServerConfigCatalog.TaxBossAppId, cancellationToken)
        };
    }

    private async Task<DateTime?> GetAnchor(CancellationToken cancellationToken) {
        string? raw = await db.Kvs.AsNoTracking()
            .Where(k => k.Key == LastRunKey)
            .Select(k => k.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedOffset)) {
            return parsedOffset.UtcDateTime;
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? parsed
            : null;
    }

    private async Task SetAnchor(DateTime value, CancellationToken cancellationToken) {
        await UpsertKv(LastRunKey, value.ToString("o", CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private async Task<ulong> GetAppTarget(string appId, CancellationToken cancellationToken) {
        string? raw = await db.Kvs.AsNoTracking()
            .Where(k => k.Key == AppTargetKey(appId))
            .Select(k => k.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0;
    }

    private async Task<string> GetStringConfig(string key, CancellationToken cancellationToken) {
        string fallback = ServerConfigCatalog.Find(key)?.Default ?? "";
        return await db.Kvs.AsNoTracking()
            .Where(k => k.Key == key)
            .Select(k => k.Value)
            .FirstOrDefaultAsync(cancellationToken) ?? fallback;
    }

    private async Task<ulong> GetIntegerConfig(string key, CancellationToken cancellationToken) {
        string value = await GetStringConfig(key, cancellationToken);
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0;
    }

    private async Task<bool> GetBooleanConfig(string key, CancellationToken cancellationToken) {
        string value = await GetStringConfig(key, cancellationToken);
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private async Task<decimal> GetPercentConfig(string key, CancellationToken cancellationToken) {
        string value = await GetStringConfig(key, cancellationToken);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? ClampPercent(parsed)
            : 0;
    }

    private async Task UpsertKv(string key, string value, CancellationToken cancellationToken) {
        DbKv? row = await db.Kvs.FirstOrDefaultAsync(k => k.Key == key, cancellationToken);
        if (row == null) {
            db.Kvs.Add(new DbKv { Key = key, Value = value });
        }
        else {
            row.Value = value;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Projection helpers
    // ---------------------------------------------------------------------------------------

    private static TaxPreview ToPreview(TaxPlan plan) => new() {
        CanRun = plan.CanRun,
        BlockedReason = plan.BlockedReason,
        DynamicRate = plan.Settings.UseDynamicRate,
        BossAppId = plan.Settings.BossAppId,
        Rate = PercentToString(plan.RatePercent),
        FixedRate = PercentToString(plan.Settings.FixedRatePercent),
        MaxDynamicRate = PercentToString(plan.Settings.MaxDynamicRatePercent),
        UsersTaxed = plan.ExpectedAccountsTaxed,
        Collected = plan.ExpectedCollection,
        AppsNeedingFunds = plan.AppsNeedingFunds,
        Distributed = plan.ExpectedDistribution,
        BossStartingBalance = plan.BossStartingBalance,
        BossEndingBalance = plan.ExpectedBossEnding
    };

    private static TaxRunResult ToRunResult(TaxPreview preview, long? cycleId) => new() {
        CanRun = preview.CanRun,
        BlockedReason = preview.BlockedReason,
        DynamicRate = preview.DynamicRate,
        BossAppId = preview.BossAppId,
        Rate = preview.Rate,
        FixedRate = preview.FixedRate,
        MaxDynamicRate = preview.MaxDynamicRate,
        UsersTaxed = preview.UsersTaxed,
        Collected = preview.Collected,
        AppsNeedingFunds = preview.AppsNeedingFunds,
        Distributed = preview.Distributed,
        BossStartingBalance = preview.BossStartingBalance,
        BossEndingBalance = preview.BossEndingBalance,
        CycleId = cycleId
    };

    /// <summary>Reports what the run actually did, read back from the committed cycle row.</summary>
    private async Task<TaxRunResult> BuildResultFromCycle(long cycleId, TaxSettings settings, CancellationToken cancellationToken) {
        DbTaxCycle? cycle = await db.TaxCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle == null) {
            return ToRunResult(BlockedPreview(settings, "The tax cycle record could not be read back."), cycleId);
        }

        return new TaxRunResult {
            CanRun = (TaxCycleStatus)cycle.Status is TaxCycleStatus.Completed or TaxCycleStatus.Running,
            BlockedReason = cycle.BlockedReason,
            DynamicRate = cycle.DynamicRate,
            BossAppId = cycle.BossAppId,
            Rate = PercentToString(cycle.RatePercent),
            FixedRate = PercentToString(settings.FixedRatePercent),
            MaxDynamicRate = PercentToString(settings.MaxDynamicRatePercent),
            UsersTaxed = cycle.AccountsTaxed,
            Collected = cycle.Collected,
            AppsNeedingFunds = cycle.AppsNeedingFunds,
            Distributed = cycle.Distributed,
            BossStartingBalance = cycle.BossStartingBalance,
            BossEndingBalance = cycle.BossEndingBalance,
            CycleId = cycle.Id
        };
    }

    private static TaxCycleRecord ToRecord(DbTaxCycle row) => new() {
        Id = row.Id,
        ScheduledForUtc = row.ScheduledForUtc,
        Manual = row.IsManual,
        Status = (TaxCycleStatus)row.Status,
        Phase = (TaxCyclePhase)row.Phase,
        StartedAt = row.StartedAt,
        CompletedAt = row.CompletedAt,
        DynamicRate = row.DynamicRate,
        Rate = PercentToString(row.RatePercent),
        BossAppId = row.BossAppId,
        Collected = row.Collected,
        AccountsTaxed = row.AccountsTaxed,
        Distributed = row.Distributed,
        AppsPaid = row.AppsPaid,
        AppsNeedingFunds = row.AppsNeedingFunds,
        BossStartingBalance = row.BossStartingBalance,
        BossEndingBalance = row.BossEndingBalance,
        Attempts = row.Attempts,
        BlockedReason = row.BlockedReason
    };

    private static TaxPreview BlockedPreview(TaxSettings settings, string reason) => new() {
        CanRun = false,
        BlockedReason = reason,
        DynamicRate = settings.UseDynamicRate,
        BossAppId = settings.BossAppId,
        Rate = "0",
        FixedRate = PercentToString(settings.FixedRatePercent),
        MaxDynamicRate = PercentToString(settings.MaxDynamicRatePercent)
    };

    private static TaxPlan EmptyPlan(TaxSettings settings, string reason) => new() {
        Settings = settings,
        BlockedReason = reason,
        BossStartingBalance = 0,
        RatePercent = 0,
        ExpectedCollection = 0,
        ExpectedAccountsTaxed = 0,
        ExpectedDistribution = 0,
        ExpectedBossEnding = 0,
        AppsNeedingFunds = 0
    };

    private static decimal ClampPercent(decimal percent) =>
        percent <= 0 ? 0 : percent >= 100 ? 100 : percent;

    private static ulong ClampToUlong(decimal value, ulong max) {
        if (value <= 0) return 0;
        decimal ceiling = max;
        return value >= ceiling ? max : (ulong)value;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value == null || value.Length <= maxLength ? value : value[..maxLength];

    private static string PercentToString(decimal percent) =>
        percent.ToString("0.########", CultureInfo.InvariantCulture);

    private static string AppTargetKey(string appId) => AppTargetPrefix + appId;
}
