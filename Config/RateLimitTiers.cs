namespace SerbleAPI.Config;

/// <summary>
/// The rate limit tiers, grouped by what an overrun costs rather than by how the endpoint is
/// written. An endpoint picks one with <c>[RateLimit(...)]</c>; untagged endpoints are not limited.
/// <list type="bullet">
///   <item><b>auth</b> — guessing wins something: passwords, TOTP codes, client secrets,
///   authorization codes, confirmation tokens.</item>
///   <item><b>costly</b> — one request spends money or sends traffic on our behalf.</item>
///   <item><b>write</b> — durable state: coin movements, trades, notes, app registrations.</item>
///   <item><b>read</b> — public reads and the batch endpoints that amplify them.</item>
///   <item><b>login</b> — guesses at one account's password or TOTP code, charged by the sign-in
///   steps rather than by an attribute. Per-ip is one client against the account: a device it
///   has signed in on before, otherwise an address. Per-identity is every other client against
///   the account together, which bounds guessing spread over many addresses.</item>
/// </list>
/// </summary>
public static class RateLimitTiers {

    public const string Auth   = "auth";
    public const string Costly = "costly";
    public const string Write  = "write";
    public const string Read   = "read";
    public const string Login  = "login";

    /// <summary>
    /// Built-in limits, used wherever configuration is silent. Per-address numbers are looser
    /// than per-account ones because an address can be a whole office behind one NAT, while an
    /// account is one person; the per-account budget is the one sized against the attack.
    /// </summary>
    private static readonly Dictionary<string, ResolvedRateLimitTier> Defaults =
        new(StringComparer.OrdinalIgnoreCase) {
            // Five attempts a minute per account is more than a person typing a TOTP code needs,
            // and turns six digits from minutes of brute force into centuries.
            [Auth] = new(Auth, true,
                PerIp:       new(true, 20, TimeSpan.FromMinutes(1)),
                PerIdentity: new(true, 5,  TimeSpan.FromMinutes(1)),
                Backoff:     new(true, TimeSpan.FromMinutes(1), 4, TimeSpan.FromHours(1), TimeSpan.FromHours(1))),

            [Costly] = new(Costly, true,
                PerIp:       new(true, 15, TimeSpan.FromMinutes(1)),
                PerIdentity: new(true, 10, TimeSpan.FromMinutes(1)),
                Backoff:     new(true, TimeSpan.FromMinutes(1), 2, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30))),

            [Write] = new(Write, true,
                PerIp:       new(true, 90, TimeSpan.FromMinutes(1)),
                PerIdentity: new(true, 60, TimeSpan.FromMinutes(1)),
                Backoff:     new(true, TimeSpan.FromSeconds(15), 2, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15))),

            [Read] = new(Read, true,
                PerIp:       new(true, 300, TimeSpan.FromMinutes(1)),
                PerIdentity: new(true, 300, TimeSpan.FromMinutes(1)),
                Backoff:     new(true, TimeSpan.FromSeconds(10), 2, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5))),

            // With this backoff one address gets about 20 guesses in its first hour, so the shared
            // budget sits above that: a single address cannot use it up and lock out the owner.
            [Login] = new(Login, true,
                PerIp:       new(true, 5,  TimeSpan.FromMinutes(1)),
                PerIdentity: new(true, 30, TimeSpan.FromHours(1)),
                Backoff:     new(true, TimeSpan.FromMinutes(1), 4, TimeSpan.FromHours(1), TimeSpan.FromHours(1)))
        };

    public static IReadOnlyCollection<string> All => Defaults.Keys;

    /// <summary>
    /// Folds <paramref name="settings"/> over the defaults. Values are clamped rather than
    /// rejected, so a typo degrades to something sane instead of disabling the limiter or
    /// locking everyone out.
    /// </summary>
    public static IReadOnlyDictionary<string, ResolvedRateLimitTier> Resolve(RateLimitSettings settings) {
        Dictionary<string, ResolvedRateLimitTier> resolved = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, ResolvedRateLimitTier fallback) in Defaults) {
            if (!settings.Tiers.TryGetValue(name, out RateLimitTierSettings? configured) || configured == null) {
                resolved[name] = fallback;
                continue;
            }

            resolved[name] = new ResolvedRateLimitTier(
                name,
                configured.Enabled ?? fallback.Enabled,
                MergeRule(configured.PerIp, fallback.PerIp),
                MergeRule(configured.PerIdentity, fallback.PerIdentity),
                MergeBackoff(configured.Backoff, fallback.Backoff));
        }

        return resolved;
    }

    private static ResolvedRateLimitRule MergeRule(RateLimitRuleSettings? configured, ResolvedRateLimitRule fallback) {
        if (configured == null) return fallback;
        return new ResolvedRateLimitRule(
            configured.Enabled ?? fallback.Enabled,
            Math.Max(1, configured.PermitLimit ?? fallback.PermitLimit),
            TimeSpan.FromSeconds(Math.Clamp(
                configured.WindowSeconds ?? (int)fallback.Window.TotalSeconds, 1, 86400)));
    }

    private static ResolvedRateLimitBackoff MergeBackoff(RateLimitBackoffSettings? configured, ResolvedRateLimitBackoff fallback) {
        if (configured == null) return fallback;

        TimeSpan baseDelay = TimeSpan.FromSeconds(Math.Clamp(
            configured.BaseSeconds ?? (int)fallback.Base.TotalSeconds, 1, 86400));
        TimeSpan max = TimeSpan.FromSeconds(Math.Clamp(
            configured.MaxSeconds ?? (int)fallback.Max.TotalSeconds, 1, 86400));

        return new ResolvedRateLimitBackoff(
            configured.Enabled ?? fallback.Enabled,
            baseDelay,
            // Below 1 would shrink the lockout on every repeat, which is the wrong direction.
            Math.Clamp(configured.Multiplier ?? fallback.Multiplier, 1, 100),
            max < baseDelay ? baseDelay : max,
            TimeSpan.FromSeconds(Math.Clamp(
                configured.DecaySeconds ?? (int)fallback.Decay.TotalSeconds, 1, 604800)));
    }
}

/// <summary>A tier with every value filled in, ready for the limiter to use.</summary>
public sealed record ResolvedRateLimitTier(
    string Name,
    bool Enabled,
    ResolvedRateLimitRule PerIp,
    ResolvedRateLimitRule PerIdentity,
    ResolvedRateLimitBackoff Backoff);

public sealed record ResolvedRateLimitRule(bool Enabled, int PermitLimit, TimeSpan Window);

public sealed record ResolvedRateLimitBackoff(
    bool Enabled,
    TimeSpan Base,
    double Multiplier,
    TimeSpan Max,
    TimeSpan Decay);
