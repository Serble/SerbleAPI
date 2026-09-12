namespace SerbleAPI.API;

/// <summary>
/// Puts an endpoint in a rate limit tier. Anything without this attribute is not limited.
/// Applied to a controller it covers every action; an action carrying its own attribute
/// overrides the controller's.
/// </summary>
/// <param name="tier">A name from <see cref="Config.RateLimitTiers"/>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RateLimitAttribute(string tier) : Attribute {

    public string Tier { get; } = tier;

    /// <summary>
    /// Permits one call consumes. Raise it where a single request fans out into many units of
    /// work, so a batch endpoint is not a cheap way to buy two hundred lookups.
    /// </summary>
    public int Cost { get; init; } = 1;
}

/// <summary>
/// Exempts one action from a tier its controller declared. For routes a third party retries on
/// its own schedule, where a 429 costs us the event rather than protecting anything.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class NoRateLimitAttribute : Attribute;
