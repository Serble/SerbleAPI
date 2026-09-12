using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// In-process fixed-window counters with a penalty box on top.
///
/// <para><b>Backoff.</b> Overrunning a window increments a violation count and locks the partition
/// out for <c>Base * Multiplier^(violations-1)</c>, capped. Refusals during a lockout do not
/// escalate it further, or a client retrying in a loop would drive itself to the ceiling for what
/// began as one overrun. Violations are forgiven after a quiet spell.</para>
///
/// <para><b>Memory.</b> Partition keys come from the network, so idle partitions are swept on a
/// timer and a hard ceiling evicts the least recently seen if the sweep is not keeping up.
/// Unblocked partitions go first, so flooding fresh addresses cannot wash out a lockout.</para>
///
/// <para><b>Scope.</b> State is per process. A second instance would get its own counters and so
/// its own copy of every budget; sharing them means moving this to the database or a shared
/// cache.</para>
/// </summary>
public class RateLimitService : IRateLimitService {

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// One caller's state for one tier and scope. Guarded by locking the instance itself: the
    /// dictionary hands every thread the same object for a given key, so that lock covers all
    /// concurrent requests from that caller and nothing else.
    /// </summary>
    private sealed class Partition {
        public DateTime WindowStart;
        public int Count;
        public int Violations;
        public DateTime LastViolation;
        public DateTime BlockedUntil;
        public DateTime LastSeen;
    }

    private readonly ConcurrentDictionary<string, Partition> _partitions = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, ResolvedRateLimitTier> _tiers;
    private readonly ILogger<RateLimitService> _logger;
    private readonly bool _enabled;
    private readonly int _maxPartitions;
    private readonly TimeSpan _idleTtl;
    private readonly object _sweepGate = new();
    private DateTime _nextSweep = DateTime.MinValue;

    public RateLimitService(IOptions<RateLimitSettings> settings, ILogger<RateLimitService> logger) {
        _logger = logger;
        RateLimitSettings value = settings.Value;
        _enabled = value.Enabled;
        _maxPartitions = Math.Max(1000, value.MaxTrackedPartitions);
        _tiers = RateLimitTiers.Resolve(value);

        // A partition may only be forgotten once it can no longer affect a decision: window rolled
        // over, lockout expired, violation count decayed. Take the longest of those across every
        // tier and double it so the arithmetic need not be exact.
        TimeSpan longest = TimeSpan.FromMinutes(1);
        foreach (ResolvedRateLimitTier tier in _tiers.Values) {
            longest = Max(longest, tier.PerIp.Window);
            longest = Max(longest, tier.PerIdentity.Window);
            longest = Max(longest, tier.Backoff.Max);
            longest = Max(longest, tier.Backoff.Decay);
        }
        _idleTtl = longest * 2;
    }

    public RateLimitDecision Check(string tier, RateLimitScope scope, string key, int cost = 1) {
        if (!_enabled) return RateLimitDecision.Unlimited;
        if (!_tiers.TryGetValue(tier, out ResolvedRateLimitTier? resolved) || !resolved.Enabled)
            return RateLimitDecision.Unlimited;

        ResolvedRateLimitRule rule = scope == RateLimitScope.Ip ? resolved.PerIp : resolved.PerIdentity;
        if (!rule.Enabled) return RateLimitDecision.Unlimited;

        if (cost < 1) cost = 1;
        DateTime now = DateTime.UtcNow;
        SweepIfDue(now);

        // Tier and scope names come from fixed sets containing no colon, so neither prefix can be
        // confused with part of a caller-supplied key.
        string id = $"{resolved.Name}:{(scope == RateLimitScope.Ip ? "ip" : "id")}:{key}";
        Partition partition = _partitions.GetOrAdd(id, _ => new Partition { WindowStart = now, LastSeen = now });

        lock (partition) {
            partition.LastSeen = now;

            if (resolved.Backoff.Enabled && partition.Violations > 0 &&
                now - partition.LastViolation > resolved.Backoff.Decay) {
                partition.Violations = 0;
            }

            if (partition.BlockedUntil > now) {
                TimeSpan left = partition.BlockedUntil - now;
                return new RateLimitDecision(false, rule.PermitLimit, 0, left, left, true);
            }

            if (now - partition.WindowStart >= rule.Window) {
                partition.WindowStart = now;
                partition.Count = 0;
            }

            TimeSpan untilReset = rule.Window - (now - partition.WindowStart);

            if (partition.Count + cost > rule.PermitLimit) {
                TimeSpan retry = untilReset;

                if (resolved.Backoff.Enabled) {
                    partition.Violations++;
                    partition.LastViolation = now;
                    TimeSpan lockout = LockoutFor(resolved.Backoff, partition.Violations);
                    if (lockout > retry) retry = lockout;
                    partition.BlockedUntil = now + retry;

                    _logger.LogWarning(
                        "Rate limit tier {Tier} locked out a {Scope} partition after overrun {Violations}; " +
                        "blocked for {Seconds}s",
                        resolved.Name, scope, partition.Violations, (int)retry.TotalSeconds);
                }

                return new RateLimitDecision(false, rule.PermitLimit, 0, retry, untilReset, false);
            }

            partition.Count += cost;
            return new RateLimitDecision(
                true, rule.PermitLimit, Math.Max(0, rule.PermitLimit - partition.Count),
                TimeSpan.Zero, untilReset, false);
        }
    }

    private static TimeSpan LockoutFor(ResolvedRateLimitBackoff backoff, int violations) {
        double seconds = backoff.Base.TotalSeconds * Math.Pow(backoff.Multiplier, Math.Max(0, violations - 1));

        // Pow runs away to infinity long before the violation count gets interesting, and a NaN
        // would build a TimeSpan that throws.
        if (double.IsNaN(seconds) || seconds > backoff.Max.TotalSeconds) {
            return backoff.Max;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Drops partitions that can no longer change a decision, then enforces the hard ceiling.
    /// Runs at most once per <see cref="SweepInterval"/> on whichever request thread notices.
    /// </summary>
    private void SweepIfDue(DateTime now) {
        if (now < _nextSweep) return;

        lock (_sweepGate) {
            if (now < _nextSweep) return;
            _nextSweep = now + SweepInterval;
        }

        foreach (KeyValuePair<string, Partition> entry in _partitions) {
            Partition partition = entry.Value;
            bool idle;
            lock (partition) {
                idle = partition.BlockedUntil <= now && now - partition.LastSeen > _idleTtl;
            }
            if (idle) _partitions.TryRemove(entry);
        }

        int excess = _partitions.Count - _maxPartitions;
        if (excess <= 0) return;

        // Over the ceiling despite the sweep, so a flood of distinct keys. Shed the least useful
        // state: not currently locked out, oldest first. Reading the fields unlocked is fine here,
        // the ordering only has to be approximately right.
        _logger.LogWarning(
            "Rate limiter is tracking {Count} partitions, over the {Max} ceiling; evicting {Excess}",
            _partitions.Count, _maxPartitions, excess);

        foreach (KeyValuePair<string, Partition> entry in _partitions.ToArray()
                     .OrderBy(e => e.Value.BlockedUntil > now)
                     .ThenBy(e => e.Value.LastSeen)
                     .Take(excess)) {
            _partitions.TryRemove(entry);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
