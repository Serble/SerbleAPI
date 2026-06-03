using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

/// <summary>Result of an app access-gate evaluation. <see cref="Reason"/> is for server logs
/// only; users always receive a single generic denial so group structure never leaks.</summary>
public class AccessDecision {
    public bool Allowed { get; init; }
    public string? Reason { get; init; }

    public static AccessDecision Allow() => new() { Allowed = true };
    public static AccessDecision Deny(string reason) => new() { Allowed = false, Reason = reason };
}

/// <summary>
/// The single place that decides whether a user may sign into an app and which group claim
/// values that app should receive. Used by both the authorize endpoint and the refresh grant.
/// </summary>
public interface IAppAccessPolicyService {
    Task<AccessDecision> Evaluate(User user, OAuthApp app);

    /// <summary>The per-app mapped <c>groups</c> claim values for this user (may be empty).</summary>
    Task<string[]> GetGroupsClaim(string userId, string appId);
}
