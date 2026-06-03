using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Oidc;

/// <summary>
/// The OIDC token endpoint. Spec-compliant single endpoint supporting the
/// <c>authorization_code</c> and <c>refresh_token</c> grants with client authentication via
/// HTTP Basic or form post (public clients use PKCE and no secret). Distinct from the legacy
/// <c>OAuthTokenController</c>, which is left untouched for backward compatibility.
/// </summary>
[ApiController]
[Route("api/v1/oauth/token")]
public class OidcTokenController(
    ILogger<OidcTokenController> logger,
    IAppRepository appRepo,
    IUserRepository userRepo,
    IOidcCodeRepository codeRepo,
    IOidcRefreshRepository refreshRepo,
    IOidcTokenService oidcTokens,
    IOidcClaimsService claims,
    IAppAccessPolicyService accessPolicy,
    IOptions<OidcSettings> settings) : ControllerManager {

    [HttpPost]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public async Task<IActionResult> Token(
        [FromForm] string? grant_type,
        [FromForm] string? code,
        [FromForm] string? redirect_uri,
        [FromForm] string? client_id,
        [FromForm] string? client_secret,
        [FromForm] string? code_verifier,
        [FromForm] string? refresh_token) {

        (string? clientId, string? clientSecret) = ResolveClient(client_id, client_secret);
        if (string.IsNullOrEmpty(clientId)) return TokenError("invalid_client", "Missing client_id");

        OAuthApp? app = await appRepo.GetOAuthApp(clientId);
        if (app == null) return TokenError("invalid_client", "Unknown client");
        if (!AuthenticateClient(app, clientSecret)) return TokenError("invalid_client", "Client authentication failed");

        return grant_type switch {
            "authorization_code" => await HandleAuthorizationCode(app, code, redirect_uri, code_verifier),
            "refresh_token"      => await HandleRefreshToken(app, refresh_token),
            _                    => TokenError("unsupported_grant_type", "Unsupported grant_type")
        };
    }

    private async Task<IActionResult> HandleAuthorizationCode(
        OAuthApp app, string? code, string? redirectUri, string? codeVerifier) {
        if (string.IsNullOrEmpty(code)) return TokenError("invalid_request", "Missing code");

        OidcAuthorizationCode? stored = await codeRepo.ConsumeCode(code);
        if (stored == null) return TokenError("invalid_grant", "Invalid or expired code");
        if (stored.ClientId != app.Id) return TokenError("invalid_grant", "Code was issued to a different client");
        if (stored.RedirectUri != (redirectUri ?? "")) return TokenError("invalid_grant", "redirect_uri mismatch");

        // PKCE: public clients (and clients configured to require it) MUST have bound a challenge
        // at authorize time; mirror the authorize-endpoint policy as a backstop, then verify.
        bool pkceRequired = app.RequirePkce
            || (app.IsPublicClient && settings.Value.RequirePkceForPublicClients);
        if (pkceRequired && string.IsNullOrEmpty(stored.CodeChallenge))
            return TokenError("invalid_grant", "PKCE required for this client");
        if (!string.IsNullOrEmpty(stored.CodeChallenge)) {
            if (string.IsNullOrEmpty(codeVerifier) || !OidcCrypto.VerifyPkceS256(codeVerifier, stored.CodeChallenge))
                return TokenError("invalid_grant", "PKCE verification failed");
        }

        User? user = await userRepo.GetUser(stored.UserId);
        if (user == null) return TokenError("invalid_grant", "User no longer exists");
        user.WithRepos(userRepo);

        string[] scopes = OidcScopes.Parse(stored.Scopes);
        string scopeString = string.Join(' ', scopes);
        string? refresh = await IssueRefreshToken(scopes, OidcCrypto.NewHandle(), app, user, scopeString,
            stored.AuthTimeUnix);
        return Ok(await BuildTokenResponse(app, user, scopes, scopeString, stored.Nonce, stored.AuthTimeUnix, refresh));
    }

    private async Task<IActionResult> HandleRefreshToken(OAuthApp app, string? refreshToken) {
        if (string.IsNullOrEmpty(refreshToken)) return TokenError("invalid_request", "Missing refresh_token");

        RefreshConsumeResult result = await refreshRepo.Consume(OidcCrypto.HashToken(refreshToken));
        if (result.Reuse) {
            logger.LogWarning("OIDC refresh token reuse detected for grant {GrantId}; grant revoked", result.GrantId);
            return TokenError("invalid_grant", "Refresh token has been revoked");
        }
        if (!result.Success) return TokenError("invalid_grant", "Invalid refresh token");
        if (result.ClientId != app.Id) {
            await refreshRepo.RevokeGrant(result.GrantId);
            return TokenError("invalid_grant", "Refresh token was issued to a different client");
        }

        User? user = await userRepo.GetUser(result.UserId);
        if (user == null) {
            await refreshRepo.RevokeGrant(result.GrantId);
            return TokenError("invalid_grant", "User no longer exists");
        }
        user.WithRepos(userRepo);

        // Re-apply the access gate on every refresh (group removal / app disable takes effect).
        AccessDecision decision = await accessPolicy.Evaluate(user, app);
        if (!decision.Allowed) {
            await refreshRepo.RevokeGrant(result.GrantId);
            logger.LogInformation("OIDC refresh denied for user {UserId} on app {AppId}: {Reason}",
                user.Id, app.Id, decision.Reason);
            return TokenError("invalid_grant", "Access denied");
        }

        string[] scopes = OidcScopes.Parse(result.Scopes);
        string scopeString = string.Join(' ', scopes);

        string? refresh = null;
        if (OidcScopes.Contains(scopes, OidcScopes.OfflineAccess)) {
            refresh = OidcCrypto.NewHandle(48);
            long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                + (long)settings.Value.RefreshTokenLifetimeDays * 86400;
            bool stored = await refreshRepo.StoreRotation(OidcCrypto.HashToken(refresh), result.GrantId,
                app.Id, user.Id, scopeString, result.AuthTimeUnix, expiresAt);
            if (!stored) return TokenError("invalid_grant", "Refresh token has been revoked");
        }

        return Ok(await BuildTokenResponse(app, user, scopes, scopeString, null, result.AuthTimeUnix, refresh));
    }

    private async Task<string?> IssueRefreshToken(string[] scopes, string grantId, OAuthApp app, User user,
        string scopeString, long authTimeUnix) {
        if (!OidcScopes.Contains(scopes, OidcScopes.OfflineAccess)) return null;
        string refresh = OidcCrypto.NewHandle(48);
        long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            + (long)settings.Value.RefreshTokenLifetimeDays * 86400;
        await refreshRepo.Store(OidcCrypto.HashToken(refresh), grantId, app.Id, user.Id, scopeString,
            authTimeUnix, expiresAt);
        return refresh;
    }

    private async Task<OidcTokenResponse> BuildTokenResponse(
        OAuthApp app, User user, string[] scopes, string scopeString, string? nonce, long authTimeUnix,
        string? refreshToken) {
        OidcTokenResponse response = new() {
            AccessToken = oidcTokens.GenerateAccessToken(user.Id, app.Id, scopeString),
            TokenType   = "Bearer",
            ExpiresIn   = settings.Value.AccessTokenLifetimeSeconds,
            Scope       = scopeString
        };

        if (OidcScopes.Contains(scopes, OidcScopes.OpenId)) {
            List<Claim> extra = await claims.BuildIdTokenClaims(user, app.Id, scopes);
            response.IdToken = oidcTokens.GenerateIdToken(user.Id, app.Id, nonce, authTimeUnix, extra);
        }

        if (refreshToken != null) response.RefreshToken = refreshToken;

        return response;
    }

    /// <summary>Resolves client credentials, preferring HTTP Basic over form post.</summary>
    private (string? clientId, string? clientSecret) ResolveClient(string? formClientId, string? formSecret) {
        string? header = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) {
            try {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
                int sep = decoded.IndexOf(':');
                if (sep >= 0) return (decoded[..sep], decoded[(sep + 1)..]);
            }
            catch (FormatException) {
                // Fall through to form-based credentials.
            }
        }
        return (formClientId, formSecret);
    }

    private static bool AuthenticateClient(OAuthApp app, string? providedSecret) {
        if (app.IsPublicClient) return true;  // public clients authenticate via PKCE, not a secret
        return !string.IsNullOrEmpty(providedSecret)
            && OidcCrypto.FixedTimeEquals(providedSecret, app.ClientSecret);
    }

    private IActionResult TokenError(string error, string description) =>
        BadRequest(new { error, error_description = description });
}
