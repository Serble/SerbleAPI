using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

/// <summary>The outcome of asking whether a caller may sign a user in to a target app.</summary>
public enum CrossAppDecision {
    /// <summary>Issue the code.</summary>
    Allowed,
    /// <summary>Permitted by the target's settings, but the user has to approve this pair first.</summary>
    ConsentRequired,
    /// <summary>The target's settings refuse this caller outright.</summary>
    Forbidden
}

/// <summary>
/// The rules for cross-app sign-in: whether a pair of apps may use it, which scopes the target
/// receives, how its settings are validated, and how the target's grant record is updated. Pure,
/// so it is unit-tested directly.
/// </summary>
public static class CrossAppRules {

    /// <summary>Upper bound on a target's caller allowlist.</summary>
    public const int MaxAllowedCallers = 50;

    /// <summary>App ids are GUID strings; the column they are stored against is 64 wide.</summary>
    private const int MaxAppIdLength = 64;

    /// <summary>What a target receives when it hasn't said: enough to know who the user is.</summary>
    public static readonly string[] DefaultScopes = ["user_info"];

    /// <summary>
    /// The Serble permissions (legacy scope ids) the target receives: its configured ones, or the
    /// default. A cross-app code carries these whichever endpoint the target redeems it at.
    /// </summary>
    public static string[] EffectiveScopes(OAuthApp target) {
        string[] configured = ParseList(target.CrossAppScopes);
        return configured.Length > 0 ? configured : DefaultScopes;
    }

    /// <summary>
    /// What a cross-app code grants when the target redeems it through OIDC. OIDC access tokens
    /// are only accepted at userinfo, so Serble permissions beyond who the user is don't apply:
    /// the target always gets identity plus a refresh token (as a legacy redemption always does),
    /// and the email claim when it was given the account-information permission.
    /// </summary>
    public static string[] OidcScopesFor(IEnumerable<string> legacyScopes) {
        string[] scopes = legacyScopes.ToArray();
        bool accountInfo = scopes.Contains("user_info") || scopes.Contains("full_access");
        return accountInfo
            ? [OidcScopes.OpenId, OidcScopes.Profile, OidcScopes.Email, OidcScopes.OfflineAccess]
            : [OidcScopes.OpenId, OidcScopes.Profile, OidcScopes.OfflineAccess];
    }

    /// <summary>
    /// Decides whether <paramref name="caller"/> may sign a user in to <paramref name="target"/>.
    /// Official→official needs no consent, which mirrors signing in to an official app directly.
    /// Every other pair the target's policy permits needs the user's stored consent.
    /// </summary>
    public static CrossAppDecision Decide(OAuthApp caller, OAuthApp target, bool userHasConsented) {
        if (caller.Id == target.Id) return CrossAppDecision.Forbidden;
        if (target.CrossAppPolicy == CrossAppPolicy.Disabled) return CrossAppDecision.Forbidden;
        if (target.CrossAppAllowedCallers.Count > 0 && !target.CrossAppAllowedCallers.Contains(caller.Id))
            return CrossAppDecision.Forbidden;
        if (target.CrossAppPolicy == CrossAppPolicy.OfficialOnly && !caller.IsOfficial)
            return CrossAppDecision.Forbidden;
        if (caller.IsOfficial && target.IsOfficial) return CrossAppDecision.Allowed;
        return userHasConsented ? CrossAppDecision.Allowed : CrossAppDecision.ConsentRequired;
    }

    // -------- Settings --------

    public static bool TryParsePolicy(string value, out CrossAppPolicy policy) {
        switch (value.Trim().ToLowerInvariant()) {
            case "disabled":         policy = CrossAppPolicy.Disabled;       return true;
            case "official_only":    policy = CrossAppPolicy.OfficialOnly;   return true;
            case "any_with_consent": policy = CrossAppPolicy.AnyWithConsent; return true;
            default:                 policy = CrossAppPolicy.Disabled;       return false;
        }
    }

    public static bool TrySetPolicy(OAuthApp app, CrossAppPolicy policy, out string? error) {
        error = null;
        if (!Enum.IsDefined(policy)) { error = "Unknown cross-app policy"; return false; }
        app.CrossAppPolicy = policy;
        return true;
    }

    /// <summary>
    /// Sets the Serble permissions the target receives, as space-separated legacy scope ids.
    /// Empty resets to the default.
    /// </summary>
    public static bool TrySetScopes(OAuthApp app, string? value, out string? error) {
        error = null;
        string[] scopes = ParseList(value);
        if (scopes.Length == 0) { app.CrossAppScopes = null; return true; }

        string[] invalid = scopes.Where(s => !ScopeHandler.Scopes.Contains(s)).ToArray();
        if (invalid.Length > 0) {
            error = $"Invalid scope(s): {string.Join(", ", invalid)}";
            return false;
        }
        app.CrossAppScopes = string.Join(' ', scopes);
        return true;
    }

    /// <summary>Sets the caller allowlist. Empty means any caller the policy allows.</summary>
    public static bool TrySetAllowedCallers(OAuthApp app, IEnumerable<string>? ids, out string? error) {
        error = null;
        List<string> list = (ids ?? [])
            .Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct()
            .ToList();
        if (list.Count > MaxAllowedCallers) {
            error = $"At most {MaxAllowedCallers} allowed callers";
            return false;
        }
        if (list.Any(i => i.Length > MaxAppIdLength || i.Any(char.IsWhiteSpace))) {
            error = "Invalid app id in allowed callers";
            return false;
        }
        if (list.Contains(app.Id)) {
            error = "An app cannot be its own cross-app caller";
            return false;
        }
        app.CrossAppAllowedCallers = list;
        return true;
    }

    public static string[] ParseList(string? value) =>
        (value ?? "")
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .ToArray();

    // -------- The target's grant record --------

    /// <summary>
    /// The grant record to write for the target when it redeems a cross-app code, in the notation
    /// of the endpoint it redeemed at, or null when the existing record already does the job.
    ///
    /// <para>A grant of the other flow is left alone. Code redemption only checks that a grant for
    /// the app exists, and rewriting it in the other notation would break the flow it came from:
    /// an OIDC grant replaced by a legacy one would no longer let the user skip OIDC consent.
    /// A grant of the same flow is widened to cover the target's cross-app scopes, never narrowed,
    /// since <see cref="User.AuthorizeApp(AuthorizedApp)"/> replaces the row.</para>
    /// </summary>
    public static AuthorizedApp? GrantToRecord(string targetAppId, AuthorizedAppGrantType type, string[] scopes,
        AuthorizedApp? existing) {
        // `is null`, not `==`: AuthorizedApp's == overload dereferences both sides.
        if (existing is not null && existing.GrantType != type) return null;

        string[] current = existing?.ScopeIds ?? [];
        if (existing is not null && scopes.All(s => current.Contains(s))) return null;

        string[] merged = current.Concat(scopes).Distinct().ToArray();
        string stored = type == AuthorizedAppGrantType.Oidc
            ? string.Join(' ', merged)
            : ScopeHandler.ListOfScopeIdsToString(merged);
        return new AuthorizedApp(targetAppId, stored) { GrantType = type };
    }
}
