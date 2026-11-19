namespace SerbleAPI.Data;

/// <summary>
/// The OpenID Connect scopes Serble understands. An OIDC request may also ask for Serble
/// permissions by their <see cref="ScopeHandler.Scopes"/> ids; those are what the API checks
/// when the access token is used, and the OIDC scopes govern the id token and userinfo.
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

    /// <summary>Every scope an OIDC request may ask for: the OIDC ones, then Serble permissions.</summary>
    public static readonly string[] Supported = [..All, ..ScopeHandler.Scopes];

    /// <summary>Parses a requested scope string, dropping scopes Serble doesn't know.</summary>
    public static string[] ParseSupported(string? scope) =>
        Parse(scope).Where(s => Supported.Contains(s, StringComparer.Ordinal)).ToArray();

    public static bool Contains(IEnumerable<string> scopes, string scope) =>
        scopes.Contains(scope, StringComparer.Ordinal);
}
