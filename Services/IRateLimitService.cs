namespace SerbleAPI.Services;

/// <summary>Which of a tier's two budgets a check draws from.</summary>
public enum RateLimitScope {
    /// <summary>The caller's network address.</summary>
    Ip,

    /// <summary>The account, app or login name behind the request.</summary>
    Identity
}

/// <summary>
/// The outcome of one check. A check consumes its permits whether or not it is allowed, so this
/// answers for that request rather than predicting the next one.
/// </summary>
/// <param name="Limit">Permits the matching window allows, or 0 when nothing was tracked.</param>
/// <param name="RetryAfter">How long before a retry can succeed. Zero when allowed.</param>
/// <param name="Reset">How long until the current window rolls over.</param>
/// <param name="Penalised">
/// Refused by an existing lockout rather than by overrunning just now. Repeat refusals during a
/// lockout do not escalate it further.
/// </param>
public readonly record struct RateLimitDecision(
    bool Allowed,
    int Limit,
    int Remaining,
    TimeSpan RetryAfter,
    TimeSpan Reset,
    bool Penalised) {

    /// <summary>Nothing applied: limiting is off, the tier is off, or the rule is off.</summary>
    public static RateLimitDecision Unlimited => new(true, 0, 0, TimeSpan.Zero, TimeSpan.Zero, false);

    /// <summary>Whether a rule applied, and so whether the counts are worth reporting.</summary>
    public bool Tracked => Limit > 0;
}

/// <summary>
/// Fixed-window rate limiting with an escalating lockout for repeat offenders.
/// <para>
/// Most callers never touch this: <see cref="API.RateLimitMiddleware"/> checks both of a tier's
/// budgets for any endpoint carrying a tier attribute. It is injectable for the cases the
/// middleware cannot reach, where the thing worth counting only becomes known inside the handler.
/// </para>
/// </summary>
public interface IRateLimitService {

    /// <summary>
    /// Draws <paramref name="cost"/> permits against one budget. <paramref name="key"/> is scoped
    /// by tier and scope internally, so callers need not avoid collisions between tiers.
    /// </summary>
    RateLimitDecision Check(string tier, RateLimitScope scope, string key, int cost = 1);
}
