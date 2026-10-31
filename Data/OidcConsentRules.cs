using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

/// <summary>
/// When an OIDC authorization may complete without showing the consent screen, and how a new
/// approval combines with the grant already on record.
/// </summary>
public static class OidcConsentRules {
    /// <summary>
    /// Consent can be skipped for an official app, or when the user's existing OIDC grant already
    /// covers every requested scope, unless the client sent <c>prompt=consent</c>. A legacy grant
    /// never counts: its scopes are a different namespace.
    /// </summary>
    public static bool CanSkipConsent(bool appIsOfficial, string? prompt, string[] requestedScopes, AuthorizedApp? existingGrant) {
        if (OidcScopes.Contains(OidcScopes.Parse(prompt), "consent")) return false;
        if (appIsOfficial) return true;
        // `is null`, not `==`: AuthorizedApp's == overload dereferences both sides.
        if (existingGrant is null || existingGrant.GrantType != AuthorizedAppGrantType.Oidc) return false;
        string[] granted = existingGrant.ScopeIds;
        return requestedScopes.All(s => OidcScopes.Contains(granted, s));
    }

    /// <summary>
    /// The scope string to store after an approval: the union with an existing OIDC grant, so an
    /// app asking for different subsets on different sign-ins doesn't shrink the grant and
    /// re-prompt. A legacy grant is replaced rather than merged.
    /// </summary>
    public static string MergedGrantScopes(string[] requestedScopes, AuthorizedApp? existingGrant) {
        IEnumerable<string> scopes = requestedScopes;
        if (existingGrant?.GrantType == AuthorizedAppGrantType.Oidc)
            scopes = existingGrant.ScopeIds.Concat(requestedScopes);
        return string.Join(' ', scopes.Distinct());
    }
}
