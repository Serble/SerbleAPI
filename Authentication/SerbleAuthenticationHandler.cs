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
using SerbleAPI.Services.Impl;

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
    IAccountStateService accountState)
    : AuthenticationHandler<SerbleAuthenticationOptions>(options, logger, encoder) {

    public const string SchemeName = "Serble";

    /// <summary>
    /// Cache key prefix for the per-user account-state lookup. Public so that the admin routes which
    /// change that state can drop the entry and have it take effect at once.
    /// </summary>
    public const string AccountStateCachePrefix = AccountStateService.CachePrefix;

    /// <summary>
    /// How long an account-state lookup is reused: the window in which a token belonging to a
    /// just-disabled or just-signed-out account still works. See <see cref="AccountStateService.Ttl"/>.
    /// </summary>
    public static readonly TimeSpan AccountStateTtl = AccountStateService.Ttl;

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
        if (!await accountState.IsTokenUsable(userId!, issuedAt)) {
            return AuthenticateResult.Fail("Account is disabled or token has been revoked");
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
        // A third-party app holding a refresh token will exchange it for a fresh access token and
        // carry on; ending that is what de-authorising the app is for.
        if (!await accountState.IsTokenUsable(appUserId!, issuedAt))
            return AuthenticateResult.Fail("Account is disabled or token has been revoked");

        List<Claim> claims = [
            new Claim("userid",    appUserId!),
            new Claim("auth_type", "App"),
            new Claim("scope",     scope)
        ];
        // appid is absent on tokens issued before it was added; official-app checks treat those as non-official.
        if (!string.IsNullOrEmpty(appId)) claims.Add(new Claim("appid", appId));
        return BuildTicket(claims);
    }

    private AuthenticateResult BuildTicket(List<Claim> claims) {
        ClaimsIdentity identity = new(claims, Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
