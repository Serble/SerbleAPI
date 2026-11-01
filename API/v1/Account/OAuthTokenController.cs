using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account; 

// ReSharper disable InconsistentNaming

/// <summary>
/// The legacy Serble OAuth token endpoints, kept for apps that predate OIDC.
///
/// <para>Only failed client authentication is rate limited, as on <c>OidcTokenController</c>.
/// Every client calls these from its own server, so a per-address budget on all requests is shared
/// by all of that client's users: a burst of sign-ins, or an app that refreshes on every request,
/// locks the whole client out. Nothing else here is guessable: codes and refresh tokens are signed
/// JWTs or long random handles.</para>
/// </summary>
[ApiController]
[Route("api/v1/oauth/token")]
public class OAuthTokenController(
    ILogger<OAuthTokenController> logger,
    ITokenService tokens,
    IAppRepository appRepo,
    IUserRepository users,
    IRateLimitService rateLimit,
    IOidcCodeRepository codeRepo) : ControllerManager {

    [HttpPost("refresh2")]
    public Task<ActionResult<AccessTokenResponse>> RequestTokensForm(
        [FromForm(Name = "code")] string code,
        [FromForm(Name = "client_id")] string clientId,
        [FromForm(Name = "client_secret")] string clientSecret,
        [FromForm(Name = "grant_type")] string grantType) => RequestTokens(code, clientId, clientSecret, grantType);

    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> RequestTokens(
        [FromQuery] string code,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        (OAuthApp? app, ActionResult? clientError) = await AuthenticateClient(client_id, client_secret);
        if (clientError != null) return clientError;

        // Legacy codes are JWTs. A code without a dot is a cross-app code: a stored, single-use code
        // that both this endpoint and the OIDC one accept, so the target redeems it wherever it
        // already redeems codes.
        if (!string.IsNullOrEmpty(code) && !code.Contains('.'))
            return await RedeemCrossAppCode(app!, code, grant_type);

        logger.LogDebug("Validating oauth code: " + code);
        if (!tokens.ValidateAuthorizationToken(code, client_id, out string? userId, out string scope,
                out string reason)) {
            return BadRequest("Invalid authorization code: " + reason);
        }
        
        if (grant_type != "authorization_code") {
            return BadRequest("Invalid grant_type, must be 'authorization_code'");
        }

        User? user = await users.GetUser(userId!);
        if (user == null) {
            return BadRequest("User not found");
        }

        // The code is a stateless JWT, so de-authorising the app cannot invalidate one already
        // issued. Without this the app simply re-runs the exchange and gets working tokens back,
        // which makes the revoke button on the authorized-apps page do nothing. The refresh
        // exchange below has always checked this; the code exchange did not.
        AuthorizedApp[] authedApps = await user.GetAuthorizedApps();
        if (authedApps.All(authorizedApp => authorizedApp.AppId != app!.Id)) {
            return BadRequest("App is not authorized");
        }

        return Ok(new AccessTokenResponse {
            ExpiresIn = 87600,
            AccessToken = tokens.GenerateAccessToken(userId!, client_id, scope),
            RefreshToken = tokens.GenerateRefreshToken(userId!, client_id, scope),
            TokenType = "bearer"
        });
    }

    /// <summary>
    /// Redeems a cross-app code for legacy tokens. The code carries Serble permissions (legacy scope
    /// ids). The target's grant is recorded here, in legacy notation, so the user sees the app on
    /// their authorized-apps page and can withdraw it exactly as after a direct sign-in, and the
    /// refresh exchange below finds it as usual.
    /// </summary>
    private async Task<ActionResult<AccessTokenResponse>> RedeemCrossAppCode(OAuthApp app, string code, string grantType) {
        if (grantType != "authorization_code") {
            return BadRequest("Invalid grant_type, must be 'authorization_code'");
        }

        OidcAuthorizationCode? stored = await codeRepo.ConsumeCrossAppCode(code, app.Id);
        if (stored == null) {
            return BadRequest("Invalid authorization code: Unknown, expired, already used, or issued to another app");
        }

        User? user = await users.GetUser(stored.UserId);
        if (user == null) {
            return BadRequest("User not found");
        }

        string[] scopeIds = CrossAppRules.ParseList(stored.Scopes);
        AuthorizedApp? existing = (await user.GetAuthorizedApps()).FirstOrDefault(a => a.AppId == app.Id);
        AuthorizedApp? record = CrossAppRules.GrantToRecord(app.Id, AuthorizedAppGrantType.Legacy, scopeIds, existing);
        if (record is not null) await user.AuthorizeApp(record);

        string scope = ScopeHandler.ListOfScopeIdsToString(scopeIds);
        return Ok(new AccessTokenResponse {
            ExpiresIn = 87600,
            AccessToken = tokens.GenerateAccessToken(user.Id, app.Id, scope),
            RefreshToken = tokens.GenerateRefreshToken(user.Id, app.Id, scope),
            TokenType = "bearer",
            ViaApp = stored.ViaClientId
        });
    }
    
    [HttpPost("access2")]
    public Task<ActionResult<AccessTokenResponse>> RequestAccessForm(
        [FromForm(Name = "refresh_token")] string refreshToken,
        [FromForm(Name = "client_id")] string clientId,
        [FromForm(Name = "client_secret")] string clientSecret,
        [FromForm(Name = "grant_type")] string grantType) => RequestAccess(refreshToken, clientId, clientSecret, grantType);
    
    [HttpPost("access")]
    public async Task<ActionResult<AccessTokenResponse>> RequestAccess(
        [FromQuery] string refresh_token,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        (OAuthApp? app, ActionResult? clientError) = await AuthenticateClient(client_id, client_secret);
        if (clientError != null) return clientError;

        if (!tokens.ValidateRefreshToken(refresh_token, client_id, out string? userId, out string scope)) {
            return BadRequest("Invalid authorization code");
        }
        
        User? user = await users.GetUser(userId!);
        if (user == null) {
            return BadRequest("User not found");
        }
        
        if (grant_type != "authorization_code") {
            return BadRequest("Invalid grant_type, must be 'authorization_code'");
        }
        
        AuthorizedApp[] authedApps = await user.GetAuthorizedApps();
        if (authedApps.All(authorizedApp => authorizedApp.AppId != app!.Id)) {
            return BadRequest("App is not authorized");
        }

        return Ok(new AccessTokenResponse {
            ExpiresIn = 1,
            AccessToken = tokens.GenerateAccessToken(user.Id, client_id, scope),
            RefreshToken = refresh_token,
            TokenType = "bearer"
        });
    }

    /// <summary>
    /// Checks the address still has failed-authentication budget, then authenticates the client.
    /// Only a failure is charged, so a client's legitimate traffic never spends its own budget.
    /// </summary>
    private async Task<(OAuthApp? app, ActionResult? error)> AuthenticateClient(string? clientId, string? clientSecret) {
        string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        RateLimitDecision limit = rateLimit.Peek(RateLimitTiers.Auth, RateLimitScope.Ip, ip);
        if (!limit.Allowed) return (null, TooManyRequests(limit.RetryAfter, new { error = "too_many_requests" }));

        OAuthApp? app = string.IsNullOrEmpty(clientId) ? null : await appRepo.GetOAuthApp(clientId);
        if (app == null) {
            rateLimit.Check(RateLimitTiers.Auth, RateLimitScope.Ip, ip);
            return (null, BadRequest("Invalid client_id"));
        }

        if (string.IsNullOrEmpty(clientSecret) || !OidcCrypto.FixedTimeEquals(clientSecret, app.ClientSecret)) {
            rateLimit.Check(RateLimitTiers.Auth, RateLimitScope.Ip, ip);
            return (null, BadRequest("Invalid client_secret"));
        }

        return (app, null);
    }
}
