using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SerbleAPI.Services;

namespace SerbleAPI.Authentication;

/// <summary>
/// Options bag for the Serble authentication scheme (no configuration needed).
/// </summary>
public class SerbleAuthenticationOptions : AuthenticationSchemeOptions { }

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
    ITokenService tokens)
    : AuthenticationHandler<SerbleAuthenticationOptions>(options, logger, encoder) {

    public const string SchemeName = "Serble";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        // Primary: custom SerbleAuth header
        if (Request.Headers.TryGetValue("SerbleAuth", out var serbleAuthValues))
            return Task.FromResult(HandleSerbleAuthHeader(serbleAuthValues.ToString()));

        // Secondary: standard Authorization header
        if (Request.Headers.TryGetValue("Authorization", out var authValues))
            return Task.FromResult(HandleAuthorizationHeader(authValues.ToString()));

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private AuthenticateResult HandleSerbleAuthHeader(string header) {
        string[] parts = header.Split(' ', 2);
        if (parts.Length != 2)
            return AuthenticateResult.Fail("SerbleAuth header must be in format 'TYPE TOKEN'");

        return parts[0] switch {
            "User" => AuthenticateAsUser(parts[1]),
            "App"  => AuthenticateAsApp(parts[1]),
            _      => AuthenticateResult.Fail($"Unknown SerbleAuth type '{parts[0]}'")
        };
    }

    private AuthenticateResult HandleAuthorizationHeader(string header) {
        string[] parts = header.Split(' ', 2);
        if (parts.Length != 2)
            return AuthenticateResult.NoResult();

        // Only intercept Bearer tokens; Basic auth is handled by the password endpoint
        if (!parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string token = parts[1];

        // Try user token first, fall back to app token
        AuthenticateResult userResult = AuthenticateAsUser(token);
        return userResult.Succeeded ? userResult : AuthenticateAsApp(token);
    }

    private AuthenticateResult AuthenticateAsUser(string token) {
        if (!tokens.ValidateLoginToken(token, out var user) || user == null)
            return AuthenticateResult.Fail("Invalid user token");

        return BuildTicket([
            new Claim("userid",    user.Id),
            new Claim("auth_type", "User"),
            new Claim("scope",     "1")  // User tokens always carry full_access
        ]);
    }

    private AuthenticateResult AuthenticateAsApp(string token) {
        if (!tokens.ValidateAccessToken(token, out var appUser, out string scope) || appUser == null)
            return AuthenticateResult.Fail("Invalid app access token");

        return BuildTicket([
            new Claim("userid",    appUser.Id),
            new Claim("auth_type", "App"),
            new Claim("scope",     scope)
        ]);
    }

    private AuthenticateResult BuildTicket(List<Claim> claims) {
        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
