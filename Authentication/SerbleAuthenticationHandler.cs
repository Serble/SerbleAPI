using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Repositories.Impl;
using SerbleAPI.Services;

namespace SerbleAPI.Authentication;

/// <summary>
/// Options bag for the Serble authentication scheme (no configuration needed).
/// </summary>
public class SerbleAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Custom ASP.NET authentication handler that supports two header formats:
///
///   SerbleAuth: User &lt;token&gt;   — direct user login JWT (full_access)
///   SerbleAuth: App &lt;token&gt;    — OAuth access JWT (scoped)
///
///   Authorization: Bearer &lt;token&gt; — tries user token first, then app token,
///                                     so both token types work with the standard
///                                     Authorization header (full backwards compat).
///
/// On success, HttpContext.User is populated with the following claims:
///   userid    — the authenticated user's ID
///   auth_type — "User" or "App"
///   scope     — the raw scope bitmask string (e.g. "10000000")
/// </summary>
public class SerbleAuthenticationHandler(
    IOptionsMonitor<SerbleAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenService tokens,
    IAppApiKeyRepository apiKeys,
    IUserRepository users,
    IMemoryCache cache)
    : AuthenticationHandler<SerbleAuthenticationOptions>(options, logger, encoder) {

    public const string SchemeName = "Serble";

    /// <summary>
    /// Cache key prefix for the per-user account-state lookup. Public so that the admin routes which
    /// change that state can drop the entry and have it take effect at once.
    /// </summary>
    public const string AccountStateCachePrefix = "authstate:usable:";

    /// <summary>
    /// How long an account-state lookup is reused: the window in which a token belonging to a
    /// just-disabled or just-signed-out account still works. Not zero, because the alternative is a
    /// user row read on every authenticated request. Routes that change the state drop the entry, so
    /// this only bounds other replicas.
    /// </summary>
    public static readonly TimeSpan AccountStateTtl = TimeSpan.FromSeconds(30);

    /// <param name="Usable">Whether the account exists and is not disabled.</param>
    /// <param name="TokensValidFrom">The revocation cut-off, or null if nothing has been revoked.</param>
    private readonly record struct AccountState(bool Usable, DateTime? TokensValidFrom);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        // Primary: custom SerbleAuth header
        if (Request.Headers.TryGetValue("SerbleAuth", out StringValues serbleAuthValues))
            return await HandleSerbleAuthHeader(serbleAuthValues.ToString());

        // Secondary: standard Authorization header
        if (Request.Headers.TryGetValue("Authorization", out StringValues authValues)) {
            return await HandleAuthorizationHeader(authValues.ToString());
        }

        return AuthenticateResult.NoResult();
    }

    private async Task<AuthenticateResult> HandleSerbleAuthHeader(string header) {
        string[] parts = header.Split(' ', 2);
        if (parts.Length != 2)
            return AuthenticateResult.Fail("SerbleAuth header must be in format 'TYPE TOKEN'");

        return parts[0] switch {
            "User"   => await AuthenticateAsUser(parts[1]),
            "App"    => await AuthenticateAsApp(parts[1]),
            "ApiKey" => await AuthenticateAsApiKey(parts[1]),
            _        => AuthenticateResult.Fail($"Unknown SerbleAuth type '{parts[0]}'")
        };
    }

    private async Task<AuthenticateResult> HandleAuthorizationHeader(string header) {
        string[] parts = header.Split(' ', 2);
        if (parts.Length != 2) {
            return AuthenticateResult.NoResult();
        }

        // Only intercept Bearer tokens; Basic auth is handled by the password endpoint
        if (!parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase)) {
            return AuthenticateResult.NoResult();
        }

        string token = parts[1];

        // App API keys have a recognisable prefix — authenticate the app as itself.
        if (token.StartsWith(AppApiKeyRepository.KeyPrefixLiteral, StringComparison.Ordinal))
            return await AuthenticateAsApiKey(token);

        // Try user token first, fall back to app token
        AuthenticateResult userResult = await AuthenticateAsUser(token);
        return userResult.Succeeded ? userResult : await AuthenticateAsApp(token);
    }

    private async Task<AuthenticateResult> AuthenticateAsApiKey(string key) {
        (string appId, string keyId)? resolved = await apiKeys.ResolveKey(key);
        if (resolved == null)
            return AuthenticateResult.Fail("Invalid app API key");

        return BuildTicket([
            new Claim("appid",     resolved.Value.appId),
            new Claim("auth_type", "ApiKey"),
            new Claim("apikeyid",  resolved.Value.keyId)
        ]);
    }

    private async Task<AuthenticateResult> AuthenticateAsUser(string token) {
        if (!tokens.ValidateLoginToken(token, out string? userId, out DateTime? issuedAt)) {
            return AuthenticateResult.Fail("Invalid user token");
        }
        AccountState state = await GetAccountState(userId!);
        if (!state.Usable) {
            return AuthenticateResult.Fail("Account is disabled");
        }
        if (IsRevoked(state, issuedAt)) {
            return AuthenticateResult.Fail("Token has been revoked");
        }

        return BuildTicket([
            new Claim("userid",    userId!),
            new Claim("auth_type", "User"),
            new Claim("scope",     "1")  // User tokens always carry full_access
        ]);
    }

    private async Task<AuthenticateResult> AuthenticateAsApp(string token) {
        if (!tokens.ValidateAccessToken(token, out string? appUserId, out string? appId, out string scope,
                out DateTime? issuedAt))
            return AuthenticateResult.Fail("Invalid app access token");
        // An app token acts on behalf of a user, so disabling the account stops the app's token too.
        AccountState state = await GetAccountState(appUserId!);
        if (!state.Usable)
            return AuthenticateResult.Fail("Account is disabled");
        // A third-party app holding a refresh token will exchange it for a fresh access token and
        // carry on; ending that is what de-authorising the app is for.
        if (IsRevoked(state, issuedAt))
            return AuthenticateResult.Fail("Token has been revoked");

        List<Claim> claims = [
            new Claim("userid",    appUserId!),
            new Claim("auth_type", "App"),
            new Claim("scope",     scope)
        ];
        // appid is absent on tokens issued before it was added; official-app checks treat those as non-official.
        if (!string.IsNullOrEmpty(appId)) claims.Add(new Claim("appid", appId));
        return BuildTicket(claims);
    }

    /// <summary>
    /// What the account behind a token currently says about it: a valid signature only proves the
    /// token was issued. Fails closed — a user row that cannot be found is unusable, which covers a
    /// token outliving the account it names, and a lookup that throws propagates rather than passing.
    /// </summary>
    private async Task<AccountState> GetAccountState(string userId) {
        if (string.IsNullOrEmpty(userId)) return new AccountState(false, null);

        string cacheKey = AccountStateCachePrefix + userId;
        if (cache.TryGetValue(cacheKey, out AccountState cached)) return cached;

        User? user = await users.GetUser(userId);
        AccountState state = user == null || user.IsDisabled()
            ? new AccountState(false, null)
            : new AccountState(true, user.TokensValidFrom);
        cache.Set(cacheKey, state, AccountStateTtl);
        return state;
    }

    /// <summary>
    /// Whether a token predates the account's revocation cut-off. A token with no issue time to
    /// compare cannot be shown to postdate the cut-off, so it is rejected once one exists.
    /// </summary>
    private static bool IsRevoked(AccountState state, DateTime? issuedAt) {
        if (state.TokensValidFrom is not { } cutoff) return false;
        return issuedAt is not { } iat || iat < cutoff;
    }

    private AuthenticateResult BuildTicket(List<Claim> claims) {
        ClaimsIdentity identity = new(claims, Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
