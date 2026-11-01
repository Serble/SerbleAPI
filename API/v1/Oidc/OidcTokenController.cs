using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.API.v1.Oidc;

/// <summary>
/// The OIDC token endpoint. Spec-compliant single endpoint supporting the
/// <c>authorization_code</c> and <c>refresh_token</c> grants with client authentication via
/// HTTP Basic or form post (public clients use PKCE and no secret). Distinct from the legacy
/// <c>OAuthTokenController</c>, which is left untouched for backward compatibility.
///
/// <para>Also hosts the cross-app grant (<see cref="CrossAppGrantType"/>): a confidential app
/// holding a user's token asks for an authorization code for another app, which that app redeems
/// as if the user had signed in to it directly.</para>
///
/// <para>Only failed client authentication is rate limited. Every client's requests come from its
/// own server, so a per-address budget on all requests is shared by all of its users and a burst
/// of sign-ins locks the whole client out. Nothing else here is guessable: codes and refresh
/// tokens are long random handles.</para>
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
    IRateLimitService rateLimit,
    IOptions<OidcSettings> settings,
    ITokenService legacyTokens,
    IAccountStateService accountState,
    ICrossAppGrantRepository crossAppGrants,
    IOptions<ApiSettings> apiSettings) : ControllerManager {

    /// <summary>The grant type an app uses to obtain a code for another app on the user's behalf.</summary>
    public const string CrossAppGrantType = "urn:serble:params:oauth:grant-type:cross-app-code";

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
        [FromForm] string? refresh_token,
        [FromForm] string? subject_token,
        [FromForm] string? audience) {

        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        RateLimitDecision limit = rateLimit.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, ip);
        if (!limit.Allowed) return TooManyRequests(limit.RetryAfter, new { error = "too_many_requests" });

        (string? clientId, string? clientSecret) = ResolveClient(client_id, client_secret);
        if (string.IsNullOrEmpty(clientId)) return TokenError("invalid_client", "Missing client_id");

        OAuthApp? app = await appRepo.GetOAuthApp(clientId);
        if (app == null || !AuthenticateClient(app, clientSecret)) {
            rateLimit.Check(RateLimitTiers.Auth, RateLimitScope.Ip, ip);
            return TokenError("invalid_client", app == null ? "Unknown client" : "Client authentication failed");
        }

        return grant_type switch {
            "authorization_code" => await HandleAuthorizationCode(app, code, redirect_uri, code_verifier),
            "refresh_token"      => await HandleRefreshToken(app, refresh_token),
            CrossAppGrantType    => await HandleCrossAppCode(app, subject_token, audience),
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

        // A cross-app code had no browser leg to bind a PKCE challenge to. It is only ever issued
        // to a confidential client, which proved itself with its secret above; refuse it if the
        // app has since become public, since then nothing authenticates the redemption.
        bool crossApp = stored.ViaClientId != null;
        if (crossApp && app.IsPublicClient)
            return TokenError("invalid_grant", "Cross-app codes can only be redeemed by confidential clients");

        // PKCE: public clients (and clients configured to require it) MUST have bound a challenge
        // at authorize time; mirror the authorize-endpoint policy as a backstop, then verify.
        bool pkceRequired = !crossApp && (app.RequirePkce
            || (app.IsPublicClient && settings.Value.RequirePkceForPublicClients));
        if (pkceRequired && string.IsNullOrEmpty(stored.CodeChallenge))
            return TokenError("invalid_grant", "PKCE required for this client");
        if (!string.IsNullOrEmpty(stored.CodeChallenge)) {
            if (string.IsNullOrEmpty(codeVerifier) || !OidcCrypto.VerifyPkceS256(codeVerifier, stored.CodeChallenge))
                return TokenError("invalid_grant", "PKCE verification failed");
        }

        User? user = await userRepo.GetUser(stored.UserId);
        if (user == null) return TokenError("invalid_grant", "User no longer exists");
        user.WithRepos(userRepo);

        // A cross-app code carries Serble permissions; as an OIDC redemption it grants identity.
        string[] scopes = crossApp
            ? CrossAppRules.OidcScopesFor(CrossAppRules.ParseList(stored.Scopes))
            : OidcScopes.Parse(stored.Scopes);
        string scopeString = string.Join(' ', scopes);

        // A cross-app code's grant is recorded here, in this flow's notation, so the user can see the
        // app on their authorized-apps page and withdraw it exactly as after a direct sign-in. The
        // refresh grant then finds it as usual.
        if (crossApp) {
            AuthorizedApp? existing = (await user.GetAuthorizedApps()).FirstOrDefault(g => g.AppId == app.Id);
            AuthorizedApp? record = CrossAppRules.GrantToRecord(app.Id, AuthorizedAppGrantType.Oidc, scopes, existing);
            if (record is not null) await user.AuthorizeApp(record);
        }

        string? refresh = await IssueRefreshToken(scopes, OidcCrypto.NewHandle(), app, user, scopeString,
            stored.AuthTimeUnix);
        OidcTokenResponse response =
            await BuildTokenResponse(app, user, scopes, scopeString, stored.Nonce, stored.AuthTimeUnix, refresh);
        response.ViaApp = stored.ViaClientId;
        return Ok(response);
    }

    private async Task<IActionResult> HandleRefreshToken(OAuthApp app, string? refreshToken) {
        if (string.IsNullOrEmpty(refreshToken)) return TokenError("invalid_request", "Missing refresh_token");

        RefreshConsumeResult result = await refreshRepo.Consume(OidcCrypto.HashToken(refreshToken),
            TimeSpan.FromSeconds(Math.Max(0, settings.Value.RefreshTokenReuseGraceSeconds)));
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

        // The consent record is authoritative. De-authorising revokes the chains directly; this
        // catches a chain that predates consent records, or one that revocation missed.
        AuthorizedApp[] grants = await user.GetAuthorizedApps();
        if (grants.All(g => g.AppId != app.Id)) {
            await refreshRepo.RevokeGrant(result.GrantId);
            logger.LogInformation("OIDC refresh refused for user {UserId} on app {AppId}: grant was withdrawn",
                user.Id, app.Id);
            return TokenError("invalid_grant", "Access has been revoked");
        }

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

    /// <summary>
    /// Cross-app sign-in. The caller presents a token the user gave it and names a target app; if
    /// the target accepts this caller and the user may use both apps, Serble records the target's
    /// grant (as the target's own sign-in would) and returns an authorization code for the target.
    /// The caller passes the code to the target, which redeems it with its own client secret, so
    /// the caller never holds anything that works as the target.
    /// </summary>
    private async Task<IActionResult> HandleCrossAppCode(OAuthApp caller, string? subjectToken, string? audience) {
        // The caller must be able to prove who it is; anyone can claim a public client's id.
        if (caller.IsPublicClient)
            return TokenError("unauthorized_client", "Cross-app sign-in requires a confidential client");
        if (string.IsNullOrEmpty(subjectToken)) return TokenError("invalid_request", "Missing subject_token");
        if (string.IsNullOrEmpty(audience)) return TokenError("invalid_request", "Missing audience");

        // The subject token must have been issued to the caller itself, so a leaked token can't be
        // turned into sign-ins by some other app.
        (string userId, string clientId)? subject = await ResolveSubjectToken(subjectToken);
        if (subject == null || subject.Value.clientId != caller.Id)
            return TokenError("invalid_grant", "Invalid subject_token");

        OAuthApp? target = await appRepo.GetOAuthApp(audience);
        if (target == null || target.Id == caller.Id)
            return TokenError("invalid_target", "Unknown audience");
        // The target redeems the code with its secret, so it has to have one.
        if (target.IsPublicClient)
            return TokenError("invalid_target", "Target app cannot accept cross-app sign-in");

        User? user = await userRepo.GetUser(subject.Value.userId);
        if (user == null) return TokenError("invalid_grant", "User no longer exists");
        user.WithRepos(userRepo);

        // The user must still have the caller authorised (as the refresh grant checks), and must be
        // allowed into both apps under their access policies.
        AuthorizedApp[] grants = await user.GetAuthorizedApps();
        if (grants.All(g => g.AppId != caller.Id)) return TokenError("invalid_grant", "Access has been revoked");
        AccessDecision callerAccess = await accessPolicy.Evaluate(user, caller);
        AccessDecision targetAccess = await accessPolicy.Evaluate(user, target);
        if (!callerAccess.Allowed || !targetAccess.Allowed) {
            logger.LogInformation("Cross-app sign-in denied for user {UserId} from {CallerId} to {TargetId}: {Reason}",
                user.Id, caller.Id, target.Id, callerAccess.Allowed ? targetAccess.Reason : callerAccess.Reason);
            return TokenError("access_denied", "User may not use one of the apps");
        }

        bool consented = await crossAppGrants.HasGrant(user.Id, caller.Id, target.Id);
        switch (CrossAppRules.Decide(caller, target, consented)) {
            case CrossAppDecision.Forbidden:
                return TokenError("unauthorized_client", "Target app does not accept cross-app sign-in from this app");
            case CrossAppDecision.ConsentRequired:
                return BadRequest(new {
                    error             = "consent_required",
                    error_description = "The user has not allowed this app to sign them in to the target",
                    // The caller appends redirect_uri (one of its registered URIs) and state.
                    consent_url = QueryHelpers.AddQueryString(
                        apiSettings.Value.WebsiteUrl.TrimEnd('/') + "/oauth/cross-app",
                        new Dictionary<string, string?> { ["client_id"] = caller.Id, ["target"] = target.Id })
                });
        }

        // One kind of code whichever flow the target uses: stored and single-use, redeemable at the
        // OIDC token endpoint or the legacy one. It carries Serble permissions (legacy scope ids);
        // each endpoint turns them into its own notation, and records the target's grant, when the
        // target redeems it.
        OidcAuthorizationCode code = new() {
            Code         = OidcCrypto.NewHandle(),
            ClientId     = target.Id,
            UserId       = user.Id,
            // No browser leg: the target redeems without a redirect_uri, which matches "".
            RedirectUri  = "",
            Scopes       = string.Join(' ', CrossAppRules.EffectiveScopes(target)),
            AuthTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ViaClientId  = caller.Id
        };
        int expiresIn = settings.Value.AuthorizationCodeLifetimeSeconds;
        await codeRepo.StoreCode(code, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn);

        logger.LogInformation("Cross-app code issued for user {UserId} from {CallerId} to {TargetId}",
            user.Id, caller.Id, target.Id);
        return Ok(new { code = code.Code, expires_in = expiresIn });
    }

    /// <summary>
    /// Resolves a token a user gave an app to (user id, app id): an OIDC access token or a legacy
    /// OAuth access token. Applies the same disabled and revoked checks as request authentication.
    /// A legacy token too old to carry an app id can't prove which app it belongs to, so it's refused.
    /// </summary>
    private async Task<(string userId, string clientId)?> ResolveSubjectToken(string token) {
        if (oidcTokens.ValidateAccessToken(token, out OidcAccessTokenInfo? oidc) && oidc != null)
            return await accountState.IsTokenUsable(oidc.UserId, oidc.IssuedAt) ? (oidc.UserId, oidc.ClientId) : null;

        if (legacyTokens.ValidateAccessToken(token, out string? userId, out string? appId, out _, out DateTime? issuedAt)
            && !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(appId))
            return await accountState.IsTokenUsable(userId, issuedAt) ? (userId, appId) : null;

        return null;
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
