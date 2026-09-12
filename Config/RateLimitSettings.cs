namespace SerbleAPI.Config;

/// <summary>
/// Configuration for request rate limiting. Every field is optional and falls back to the tier's
/// built-in default in <see cref="RateLimitTiers"/>, so a partial section only changes what it
/// names. Endpoints opt in with a tier attribute; anything untagged is not limited.
/// </summary>
public class RateLimitSettings {

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Ceiling on tracked partitions. Keys come from the network, so this is what stops a flood
    /// of unique addresses turning the limiter into a memory leak.
    /// </summary>
    public int MaxTrackedPartitions { get; set; } = 200_000;

    /// <summary>Overrides keyed by tier name. Unknown names are ignored.</summary>
    public Dictionary<string, RateLimitTierSettings> Tiers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Overrides for one tier. A tier limits each request twice, against the caller's address and
/// against the account behind it, so one abusive account cannot hide behind many addresses and
/// one address cannot cycle through many accounts.
/// </summary>
public class RateLimitTierSettings {
    public bool? Enabled { get; set; }
    public RateLimitRuleSettings? PerIp { get; set; }
    public RateLimitRuleSettings? PerIdentity { get; set; }
    public RateLimitBackoffSettings? Backoff { get; set; }
}

/// <summary>One fixed window: at most <see cref="PermitLimit"/> permits per <see cref="WindowSeconds"/>.</summary>
public class RateLimitRuleSettings {
    public bool? Enabled { get; set; }
    public int? PermitLimit { get; set; }
    public int? WindowSeconds { get; set; }
}

/// <summary>
/// What happens to a caller that keeps overrunning. Each overrun locks the partition out for
/// <c>BaseSeconds * Multiplier^(overruns-1)</c>, capped at <see cref="MaxSeconds"/>.
/// </summary>
public class RateLimitBackoffSettings {

    /// <summary>Off means an overrun only costs the remainder of the current window.</summary>
    public bool? Enabled { get; set; }

    public int? BaseSeconds { get; set; }
    public double? Multiplier { get; set; }
    public int? MaxSeconds { get; set; }

    /// <summary>
    /// How long a partition must behave before its escalation is forgiven. Without it a single
    /// bad afternoon would hold an address at the maximum lockout indefinitely.
    /// </summary>
    public int? DecaySeconds { get; set; }
}
