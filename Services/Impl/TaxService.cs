using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

public class TaxService(SerbleDbContext db, ILogger<TaxService> logger) : ITaxService {
    private const string LastRunKey = "economy.tax._last_run_utc";
    private const string AppTargetPrefix = "economy.tax.target_balance.";
    private const int MaxCatchUpCyclesPerRun = 24;

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
        public required DbBalance Balance { get; init; }
        public required ulong TargetBalance { get; init; }
        public ulong CurrentBalanceSnapshot { get; init; }
        public ulong Deficit => TargetBalance > CurrentBalanceSnapshot ? TargetBalance - CurrentBalanceSnapshot : 0;
    }

    private sealed class TaxComputation {
        public bool CanRun { get; init; }
        public string? BlockedReason { get; init; }
        public required TaxSettings Settings { get; init; }
        public required DbBalance BossBalance { get; init; }
        public required List<DbBalance> UserBalances { get; init; }
        public required List<OfficialRecipient> Recipients { get; init; }
        public required ulong BossStartingBalance { get; init; }
        public required decimal RatePercent { get; init; }
        public required ulong Collected { get; init; }
        public required int UsersTaxed { get; init; }
        public required ulong Distributed { get; init; }
        public required int AppsNeedingFunds { get; init; }
        public required ulong BossEndingBalance { get; init; }
    }

    public async Task<OfficialAppTaxTarget> GetOfficialAppTarget(string appId, CancellationToken cancellationToken = default) {
        ulong target = await GetAppTarget(appId, cancellationToken);
        return new OfficialAppTaxTarget { AppId = appId, TargetBalance = target };
    }

    public async Task SetOfficialAppTarget(string appId, ulong targetBalance, CancellationToken cancellationToken = default) {
        await UpsertKv(AppTargetKey(appId), targetBalance.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaxPreview> PreviewTaxRun(CancellationToken cancellationToken = default) {
        TaxComputation calc = await BuildComputation(applyChanges: false, requireScheduler: false, cancellationToken);
        return ToPreview(calc);
    }

    public async Task<TaxRunResult> RunTaxNow(CancellationToken cancellationToken = default) {
        await using IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        TaxComputation calc = await BuildComputation(applyChanges: true, requireScheduler: false, cancellationToken);
        if (!calc.CanRun) {
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return ToRunResult(calc);
        }

        await UpsertKv(LastRunKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Admin-triggered tax run: mode={Mode}, ratePercent={RatePercent}%, usersTaxed={UsersTaxed}, collectedRaw={CollectedRaw} ({CollectedCoins}), appsPaid={AppsPaid}, distributedRaw={DistributedRaw} ({DistributedCoins}), bossEndingBalanceRaw={BossRaw} ({BossCoins})",
            calc.Settings.UseDynamicRate ? "dynamic" : "fixed",
            PercentToString(calc.RatePercent),
            calc.UsersTaxed,
            calc.Collected,
            CoinFixedPoint.ToCoinsString(calc.Collected),
            calc.AppsNeedingFunds,
            calc.Distributed,
            CoinFixedPoint.ToCoinsString(calc.Distributed),
            calc.BossEndingBalance,
            CoinFixedPoint.ToCoinsString(calc.BossEndingBalance));

        return ToRunResult(calc);
    }

    public async Task RunDueTaxCycles(CancellationToken cancellationToken = default) {
        await using IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        TaxSettings settings = await LoadSettings(cancellationToken);
        DateTime now = DateTime.UtcNow;
        if (!settings.SchedulerEnabled) {
            await UpsertKv(LastRunKey, now.ToString("o", CultureInfo.InvariantCulture), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return;
        }

        DateTime? lastRun = await GetLastRun(cancellationToken);
        if (lastRun == null) {
            await UpsertKv(LastRunKey, now.ToString("o", CultureInfo.InvariantCulture), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return;
        }

        int dueCycles = CountDueCycles(lastRun.Value, now, settings.Period);
        if (dueCycles <= 0) {
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return;
        }

        TaxComputation? lastCalc = null;
        for (int i = 0; i < dueCycles; i++) {
            lastCalc = await BuildComputation(applyChanges: true, requireScheduler: true, cancellationToken);
            if (!lastCalc.CanRun) break;
        }

        DateTime processedUntil = lastRun.Value.AddTicks(settings.Period.Ticks * dueCycles);
        await UpsertKv(LastRunKey, processedUntil.ToString("o", CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        if (lastCalc is { CanRun: true }) {
            logger.LogInformation(
                "Processed {CycleCount} tax cycle(s): mode={Mode}, ratePercent={RatePercent}%, usersTaxed={UsersTaxed}, collectedRaw={CollectedRaw} ({CollectedCoins}), appsPaid={AppsPaid}, distributedRaw={DistributedRaw} ({DistributedCoins}), bossEndingBalanceRaw={BossRaw} ({BossCoins})",
                dueCycles,
                lastCalc.Settings.UseDynamicRate ? "dynamic" : "fixed",
                PercentToString(lastCalc.RatePercent),
                lastCalc.UsersTaxed,
                lastCalc.Collected,
                CoinFixedPoint.ToCoinsString(lastCalc.Collected),
                lastCalc.AppsNeedingFunds,
                lastCalc.Distributed,
                CoinFixedPoint.ToCoinsString(lastCalc.Distributed),
                lastCalc.BossEndingBalance,
                CoinFixedPoint.ToCoinsString(lastCalc.BossEndingBalance));
        }
    }

    private async Task<TaxComputation> BuildComputation(bool applyChanges, bool requireScheduler, CancellationToken cancellationToken) {
        TaxSettings settings = await LoadSettings(cancellationToken);
        if (requireScheduler && !settings.SchedulerEnabled) {
            return EmptyComputation(settings, "Tax scheduler is disabled.");
        }
        if (!requireScheduler && !settings.ManualRunEnabled) {
            return EmptyComputation(settings, "BOSS app id is not configured.");
        }

        DbApp? bossApp = await db.Apps.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == settings.BossAppId, cancellationToken);
        if (bossApp == null) {
            return EmptyComputation(settings, $"Configured BOSS app '{settings.BossAppId}' does not exist.");
        }

        DbBalance bossBalance = await GetOrCreateDefaultBalance(BalanceOwnerType.App, settings.BossAppId, cancellationToken);
        ulong bossStartingBalance = bossBalance.Coins;

        List<DbBalance> userBalances = await LoadDefaultBalances(BalanceOwnerType.User, ownerIds: null, cancellationToken);
        List<OfficialRecipient> recipients = await LoadOfficialRecipients(settings.BossAppId, cancellationToken);
        int appsNeedingFunds = recipients.Count(r => r.Deficit > 0);

        ulong bossCapacity = ulong.MaxValue - bossStartingBalance;
        decimal ratePercent = settings.UseDynamicRate
            ? ComputeDynamicRatePercent(userBalances.Select(b => b.Coins).ToArray(), recipients, bossStartingBalance, bossCapacity, settings.MaxDynamicRatePercent)
            : ComputeFixedRatePercent(userBalances.Select(b => b.Coins).ToArray(), bossCapacity, settings.FixedRatePercent);

        (ulong collected, int usersTaxed) = applyChanges
            ? CollectTaxes(userBalances, bossBalance, ratePercent)
            : PreviewCollection(userBalances, ratePercent);
        if (collected > bossCapacity) {
            if (applyChanges) {
                throw new InvalidOperationException("Computed tax collection exceeded BOSS capacity.");
            }
            collected = bossCapacity;
        }

        ulong bossAfterCollection = bossStartingBalance + collected;
        (ulong distributed, _) = applyChanges
            ? DistributeBossFunds(bossBalance, recipients)
            : PreviewDistribution(bossAfterCollection, recipients);
        ulong bossEndingBalance = bossAfterCollection - distributed;

        string? blockedReason = DetermineBlockedReason(
            settings,
            recipients,
            userBalances,
            bossCapacity,
            ratePercent,
            collected,
            distributed);

        return new TaxComputation {
            CanRun = blockedReason == null,
            BlockedReason = blockedReason,
            Settings = settings,
            BossBalance = bossBalance,
            UserBalances = userBalances,
            Recipients = recipients,
            BossStartingBalance = bossStartingBalance,
            RatePercent = ratePercent,
            Collected = collected,
            UsersTaxed = usersTaxed,
            Distributed = distributed,
            AppsNeedingFunds = appsNeedingFunds,
            BossEndingBalance = bossEndingBalance
        };
    }

    private static string? DetermineBlockedReason(
        TaxSettings settings,
        IReadOnlyCollection<OfficialRecipient> recipients,
        IReadOnlyCollection<DbBalance> userBalances,
        ulong bossCapacity,
        decimal ratePercent,
        ulong collected,
        ulong distributed) {

        if (bossCapacity == 0) {
            return "The BOSS account balance is already at the maximum value, so it cannot receive any more tax.";
        }
        if (collected > 0 || distributed > 0) return null;

        int appsNeedingFunds = recipients.Count(r => r.Deficit > 0);
        bool anyUsersWithCoins = userBalances.Any(b => b.Coins > 0);

        if (settings.UseDynamicRate) {
            if (settings.MaxDynamicRatePercent == 0) {
                return "Dynamic tax is enabled, but the max dynamic tax rate setting is 0%.";
            }
            if (appsNeedingFunds == 0) {
                return "No official apps currently need funds because all configured target balances are already met.";
            }
            if (!anyUsersWithCoins) {
                return "Official apps need funds, but no users currently have any coins available to tax.";
            }
            if (ratePercent == 0) {
                return "The dynamic tax calculation resolved to 0% with the current balances.";
            }
            return "With the current balances and settings, a tax run would not collect or distribute any coins after rounding.";
        }

        if (settings.FixedRatePercent == 0) {
            return "Fixed tax mode is enabled, but the fixed tax rate setting is 0%.";
        }
        if (!anyUsersWithCoins) {
            return "The fixed tax rate is set, but no users currently have any coins available to tax.";
        }
        return "With the current balances and settings, a tax run would not collect or distribute any coins after rounding.";
    }

    private TaxPreview ToPreview(TaxComputation calc) => new() {
        CanRun = calc.CanRun,
        BlockedReason = calc.BlockedReason,
        DynamicRate = calc.Settings.UseDynamicRate,
        BossAppId = calc.Settings.BossAppId,
        Rate = PercentToString(calc.RatePercent),
        FixedRate = PercentToString(calc.Settings.FixedRatePercent),
        MaxDynamicRate = PercentToString(calc.Settings.MaxDynamicRatePercent),
        UsersTaxed = calc.UsersTaxed,
        Collected = calc.Collected,
        AppsNeedingFunds = calc.AppsNeedingFunds,
        Distributed = calc.Distributed,
        BossStartingBalance = calc.BossStartingBalance,
        BossEndingBalance = calc.BossEndingBalance
    };

    private TaxRunResult ToRunResult(TaxComputation calc) => new() {
        CanRun = calc.CanRun,
        BlockedReason = calc.BlockedReason,
        DynamicRate = calc.Settings.UseDynamicRate,
        BossAppId = calc.Settings.BossAppId,
        Rate = PercentToString(calc.RatePercent),
        FixedRate = PercentToString(calc.Settings.FixedRatePercent),
        MaxDynamicRate = PercentToString(calc.Settings.MaxDynamicRatePercent),
        UsersTaxed = calc.UsersTaxed,
        Collected = calc.Collected,
        AppsNeedingFunds = calc.AppsNeedingFunds,
        Distributed = calc.Distributed,
        BossStartingBalance = calc.BossStartingBalance,
        BossEndingBalance = calc.BossEndingBalance
    };

    private TaxComputation EmptyComputation(TaxSettings settings, string reason) => new() {
        CanRun = false,
        BlockedReason = reason,
        Settings = settings,
        BossBalance = new DbBalance {
            Id = "",
            OwnerType = (int)BalanceOwnerType.App,
            OwnerId = settings.BossAppId,
            Coins = 0,
            DateCreated = default
        },
        UserBalances = [],
        Recipients = [],
        BossStartingBalance = 0,
        RatePercent = 0,
        Collected = 0,
        UsersTaxed = 0,
        Distributed = 0,
        AppsNeedingFunds = 0,
        BossEndingBalance = 0
    };

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

    private int CountDueCycles(DateTime lastRun, DateTime now, TimeSpan period) {
        if (period <= TimeSpan.Zero || now < lastRun + period) return 0;
        long elapsedTicks = now.Ticks - lastRun.Ticks;
        long due = elapsedTicks / period.Ticks;
        return (int)Math.Clamp(due, 0, MaxCatchUpCyclesPerRun);
    }

    private async Task<List<OfficialRecipient>> LoadOfficialRecipients(string bossAppId, CancellationToken cancellationToken) {
        string[] officialAppIds = await db.Apps.AsNoTracking()
            .Where(a => a.IsOfficial && a.Id != bossAppId)
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToArrayAsync(cancellationToken);
        if (officialAppIds.Length == 0) return [];

        Dictionary<string, DbBalance> balances = (await LoadDefaultBalances(BalanceOwnerType.App, officialAppIds, cancellationToken))
            .ToDictionary(b => b.OwnerId, b => b);
        string[] targetKeys = officialAppIds.Select(AppTargetKey).ToArray();
        Dictionary<string, string> targetValues = await db.Kvs.AsNoTracking()
            .Where(k => targetKeys.Contains(k.Key))
            .ToDictionaryAsync(k => k.Key, k => k.Value, cancellationToken);

        List<OfficialRecipient> recipients = new(officialAppIds.Length);
        foreach (string appId in officialAppIds) {
            DbBalance balance = balances[appId];
            ulong target = targetValues.TryGetValue(AppTargetKey(appId), out string? raw)
                       && ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
                ? parsed
                : 0;
            recipients.Add(new OfficialRecipient {
                AppId = appId,
                Balance = balance,
                TargetBalance = target,
                CurrentBalanceSnapshot = balance.Coins
            });
        }
        return recipients;
    }

    private static decimal ComputeDynamicRatePercent(
        IReadOnlyCollection<ulong> userBalances,
        IReadOnlyCollection<OfficialRecipient> recipients,
        ulong bossStartBalance,
        ulong bossCapacity,
        decimal maxDynamicRatePercent) {

        if (maxDynamicRatePercent <= 0 || userBalances.Count == 0 || bossCapacity == 0) return 0;

        decimal totalDeficit = recipients.Sum(r => (decimal)r.Deficit);
        if (totalDeficit <= bossStartBalance) return 0;

        decimal totalUserWealth = userBalances.Sum(v => (decimal)v);
        if (totalUserWealth <= 0) return 0;

        decimal neededCollection = totalDeficit - bossStartBalance;
        decimal computedPercent = neededCollection / totalUserWealth * 100m;
        decimal requestedPercent = ClampPercent(computedPercent > maxDynamicRatePercent ? maxDynamicRatePercent : computedPercent);
        return FindLargestSafePercent(userBalances, bossCapacity, requestedPercent);
    }

    private static decimal ComputeFixedRatePercent(
        IReadOnlyCollection<ulong> userBalances,
        ulong bossCapacity,
        decimal configuredRatePercent) {
        decimal requestedPercent = ClampPercent(configuredRatePercent);
        return requestedPercent <= 0 || bossCapacity == 0
            ? 0
            : FindLargestSafePercent(userBalances, bossCapacity, requestedPercent);
    }

    private static decimal ClampPercent(decimal percent) =>
        percent <= 0 ? 0 : percent >= 100 ? 100 : percent;

    private static decimal FindLargestSafePercent(
        IReadOnlyCollection<ulong> userBalances,
        ulong bossCapacity,
        decimal maxPercent) {

        decimal clampedMax = ClampPercent(maxPercent);
        if (clampedMax <= 0 || bossCapacity == 0 || userBalances.Count == 0) return 0;
        if (PreviewCollection(userBalances, clampedMax).collected <= bossCapacity) {
            return clampedMax;
        }

        decimal low = 0;
        decimal high = clampedMax;
        for (int i = 0; i < 48; i++) {
            decimal mid = (low + high) / 2m;
            ulong collected = PreviewCollection(userBalances, mid).collected;
            if (collected <= bossCapacity) low = mid;
            else high = mid;
        }
        return low;
    }

    private static ulong ComputeDueFromPercent(ulong balance, decimal ratePercent) {
        if (balance == 0 || ratePercent <= 0) return 0;
        if (ratePercent >= 100) return balance;
        decimal dueDec = Math.Round(balance * (ratePercent / 100m), MidpointRounding.AwayFromZero);
        if (dueDec <= 0) return 0;
        if (dueDec >= balance) return balance;
        return (ulong)dueDec;
    }

    private static (ulong collected, int usersTaxed) PreviewCollection(IReadOnlyCollection<DbBalance> userBalances, decimal ratePercent) {
        if (ratePercent <= 0) return (0, 0);
        decimal clamped = ClampPercent(ratePercent);
        ulong totalCollected = 0;
        int usersTaxed = 0;
        foreach (DbBalance userBalance in userBalances) {
            ulong due = ComputeDueFromPercent(userBalance.Coins, clamped);
            if (due == 0) continue;
            usersTaxed++;
            totalCollected += due;
        }
        return (totalCollected, usersTaxed);
    }

    private static (ulong collected, int usersTaxed) PreviewCollection(IReadOnlyCollection<ulong> userBalances, decimal ratePercent) {
        if (ratePercent <= 0) return (0, 0);
        decimal clamped = ClampPercent(ratePercent);
        ulong totalCollected = 0;
        int usersTaxed = 0;
        foreach (ulong userBalance in userBalances) {
            ulong due = ComputeDueFromPercent(userBalance, clamped);
            if (due == 0) continue;
            usersTaxed++;
            totalCollected += due;
        }
        return (totalCollected, usersTaxed);
    }

    private static (ulong distributed, int appsPaid) PreviewDistribution(ulong bossAvailable, IReadOnlyCollection<OfficialRecipient> recipients) {
        Dictionary<string, ulong> payouts = ComputeEvenPayouts(recipients, bossAvailable);
        ulong totalDistributed = 0;
        int appsPaid = 0;
        foreach (ulong payout in payouts.Values) {
            if (payout == 0) continue;
            appsPaid++;
            totalDistributed += payout;
        }
        return (totalDistributed, appsPaid);
    }

    private (ulong collected, int usersTaxed) CollectTaxes(IReadOnlyCollection<DbBalance> userBalances, DbBalance bossBalance, decimal ratePercent) {
        if (ratePercent <= 0) return (0, 0);
        decimal clamped = ClampPercent(ratePercent);
        ulong totalCollected = 0;
        int usersTaxed = 0;
        DateTime createdAt = DateTime.UtcNow;
        foreach (DbBalance userBalance in userBalances.OrderBy(b => b.OwnerId)) {
            ulong due = ComputeDueFromPercent(userBalance.Coins, clamped);
            if (due == 0) continue;
            usersTaxed++;
            userBalance.Coins -= due;
            bossBalance.Coins += due;
            totalCollected += due;
            db.Transactions.Add(new DbTransaction {
                Id = Guid.NewGuid().ToString(),
                FromBalanceId = userBalance.Id,
                ToBalanceId = bossBalance.Id,
                Amount = due,
                Description = "Periodic tax collection",
                DateCreated = createdAt
            });
        }
        return (totalCollected, usersTaxed);
    }

    private (ulong distributed, int appsPaid) DistributeBossFunds(DbBalance bossBalance, IReadOnlyCollection<OfficialRecipient> recipients) {
        Dictionary<string, ulong> payouts = ComputeEvenPayouts(recipients, bossBalance.Coins);
        ulong totalDistributed = 0;
        int appsPaid = 0;
        DateTime createdAt = DateTime.UtcNow;

        foreach (OfficialRecipient recipient in recipients.OrderBy(r => r.AppId)) {
            if (!payouts.TryGetValue(recipient.AppId, out ulong payout) || payout == 0) continue;
            appsPaid++;
            bossBalance.Coins -= payout;
            recipient.Balance.Coins += payout;
            totalDistributed += payout;
            db.Transactions.Add(new DbTransaction {
                Id = Guid.NewGuid().ToString(),
                FromBalanceId = bossBalance.Id,
                ToBalanceId = recipient.Balance.Id,
                Amount = payout,
                Description = "Periodic tax payout",
                DateCreated = createdAt
            });
        }
        return (totalDistributed, appsPaid);
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

    private async Task<List<DbBalance>> LoadDefaultBalances(BalanceOwnerType ownerType, IReadOnlyCollection<string>? ownerIds, CancellationToken cancellationToken) {
        int type = (int)ownerType;
        IQueryable<DbBalance> query = db.Balances.Where(b => b.OwnerType == type);
        if (ownerIds is { Count: > 0 }) {
            query = query.Where(b => ownerIds.Contains(b.OwnerId));
        }

        List<DbBalance> rows = await query
            .OrderBy(b => b.OwnerId)
            .ThenBy(b => b.DateCreated)
            .ToListAsync(cancellationToken);

        List<DbBalance> defaults = rows
            .GroupBy(b => b.OwnerId)
            .Select(g => g.First())
            .ToList();

        if (ownerIds is { Count: > 0 }) {
            HashSet<string> have = defaults.Select(b => b.OwnerId).ToHashSet();
            foreach (string ownerId in ownerIds.Where(id => !have.Contains(id))) {
                defaults.Add(await GetOrCreateDefaultBalance(ownerType, ownerId, cancellationToken));
            }
        }

        return defaults;
    }

    private async Task<DbBalance> GetOrCreateDefaultBalance(BalanceOwnerType ownerType, string ownerId, CancellationToken cancellationToken) {
        int type = (int)ownerType;
        DbBalance? existing = await db.Balances
            .OrderBy(b => b.DateCreated)
            .FirstOrDefaultAsync(b => b.OwnerType == type && b.OwnerId == ownerId, cancellationToken);
        if (existing != null) return existing;

        DbBalance created = new() {
            Id = Guid.NewGuid().ToString(),
            OwnerType = type,
            OwnerId = ownerId,
            Coins = 0,
            DateCreated = DateTime.UtcNow
        };
        db.Balances.Add(created);
        return created;
    }

    private async Task<DateTime?> GetLastRun(CancellationToken cancellationToken) {
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

    private static string PercentToString(decimal percent) =>
        percent.ToString("0.########", CultureInfo.InvariantCulture);

    private static string AppTargetKey(string appId) => AppTargetPrefix + appId;
}
