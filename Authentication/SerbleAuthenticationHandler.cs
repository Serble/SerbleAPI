using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
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
    IAppApiKeyRepository apiKeys)
    : AuthenticationHandler<SerbleAuthenticationOptions>(options, logger, encoder) {

    public const string SchemeName = "Serble";

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
            "User"   => AuthenticateAsUser(parts[1]),
            "App"    => AuthenticateAsApp(parts[1]),
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
        AuthenticateResult userResult = AuthenticateAsUser(token);
        return userResult.Succeeded ? userResult : AuthenticateAsApp(token);
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

    private AuthenticateResult AuthenticateAsUser(string token) {
        if (!tokens.ValidateLoginToken(token, out string? userId)) {
            return AuthenticateResult.Fail("Invalid user token");
        }

        return BuildTicket([
            new Claim("userid",    userId!),
            new Claim("auth_type", "User"),
            new Claim("scope",     "1")  // User tokens always carry full_access
        ]);
    }

    private AuthenticateResult AuthenticateAsApp(string token) {
        if (!tokens.ValidateAccessToken(token, out string? appUserId, out string? appId, out string scope))
            return AuthenticateResult.Fail("Invalid app access token");

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
