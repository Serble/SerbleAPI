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
    /// How long an account-state lookup is reused. This is the window in which a token belonging to
    /// a just-disabled account still works, so it is short; it is not zero because the alternative
    /// is a user row read on every single authenticated request.
    /// <para>
    /// Bounding it this way is only necessary because tokens carry no revocation marker. Once they
    /// have a version stamped in them that a password change or a disable can bump (SA-05), that
    /// becomes the immediate mechanism and this is just a cheap first line.
    /// </para>
    /// </summary>
    public static readonly TimeSpan AccountStateTtl = TimeSpan.FromSeconds(30);

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
        if (!tokens.ValidateLoginToken(token, out string? userId)) {
            return AuthenticateResult.Fail("Invalid user token");
        }
        if (!await IsAccountUsable(userId!)) {
            return AuthenticateResult.Fail("Account is disabled");
        }

        return BuildTicket([
            new Claim("userid",    userId!),
            new Claim("auth_type", "User"),
            new Claim("scope",     "1")  // User tokens always carry full_access
        ]);
    }

    private async Task<AuthenticateResult> AuthenticateAsApp(string token) {
        if (!tokens.ValidateAccessToken(token, out string? appUserId, out string? appId, out string scope))
            return AuthenticateResult.Fail("Invalid app access token");
        // An app token acts on behalf of a user, so disabling the account has to stop the app's
        // token too — otherwise revoking access leaves every third-party grant still working.
        if (!await IsAccountUsable(appUserId!))
            return AuthenticateResult.Fail("Account is disabled");

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
    /// Whether the account behind a token may still act. A valid signature only proves the token was
    /// issued; it says nothing about whether the account still exists or is still permitted, and
    /// tokens here are long-lived, so this is the only thing standing between a disabled account and
    /// every authenticated endpoint.
    /// <para>
    /// Fails closed: a user row that cannot be found is treated as unusable, which also covers a
    /// token outliving the account it names. A lookup that throws propagates rather than being
    /// swallowed into a pass.
    /// </para>
    /// </summary>
    private async Task<bool> IsAccountUsable(string userId) {
        if (string.IsNullOrEmpty(userId)) return false;

        string cacheKey = AccountStateCachePrefix + userId;
        if (cache.TryGetValue(cacheKey, out bool cached)) return cached;

        User? user = await users.GetUser(userId);
        bool usable = user != null && !user.IsDisabled();
        cache.Set(cacheKey, usable, AccountStateTtl);
        return usable;
    }

    private AuthenticateResult BuildTicket(List<Claim> claims) {
        ClaimsIdentity identity = new(claims, Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
