namespace SerbleAPI.Data;

/// <summary>
/// The OpenID Connect scopes Serble understands, kept separate from the legacy Serble
/// bitmask scopes in <see cref="ScopeHandler"/>. OIDC requests are space-delimited scope
/// strings, so these coexist with (and are parsed alongside) the bitmask scopes.
/// </summary>
public static class OidcScopes {
    public const string OpenId        = "openid";
    public const string Profile       = "profile";
    public const string Email         = "email";
    public const string Groups        = "groups";
    public const string OfflineAccess = "offline_access";

    public static readonly string[] All = [OpenId, Profile, Email, Groups, OfflineAccess];

    /// <summary>Splits a raw space-delimited scope string into distinct, non-empty entries.</summary>
    public static string[] Parse(string? scope) =>
        (scope ?? "")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .ToArray();

    public static bool Contains(IEnumerable<string> scopes, string scope) =>
        scopes.Contains(scope, StringComparer.Ordinal);
}
