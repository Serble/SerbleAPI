using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Oidc;

/// <summary>
/// The OIDC authorization endpoint. Serble owns the whole transaction: the initial GET
/// validates the request and stores a server-side session, then the serble.net consent page
/// (which only ever receives the opaque session id) drives approve/deny. Codes are minted by
/// the backend and only ever sent to a pre-validated, exact-match redirect URI.
/// </summary>
[ApiController]
[Route("api/v1/oauth/authorize")]
public class OidcAuthorizeController(
    ILogger<OidcAuthorizeController> logger,
    IAppRepository appRepo,
    IUserRepository userRepo,
    IOidcCodeRepository codeRepo,
    IAppAccessPolicyService accessPolicy,
    IMemoryCache cache,
    IOptions<ApiSettings> apiSettings,
    IOptions<OidcSettings> oidcSettings) : ControllerManager {

    // The pre-consent session lives only long enough to complete the browser interaction.
    private static readonly MemoryCacheEntryOptions SessionExpiry =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

    private static string SessionKey(string id) => $"oidc:authsession:{id}";

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Authorize(
        [FromQuery] string? client_id,
        [FromQuery] string? redirect_uri,
        [FromQuery] string? response_type,
        [FromQuery] string? scope,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery] string? code_challenge,
        [FromQuery] string? code_challenge_method) {

        if (string.IsNullOrEmpty(client_id)) return BadRequest("Missing client_id");
        if (string.IsNullOrEmpty(redirect_uri)) return BadRequest("Missing redirect_uri");

        OAuthApp? app = await appRepo.GetOAuthApp(client_id);
        if (app == null) return BadRequest("Invalid client_id");

        // redirect_uri must exactly match a registered URI before we ever redirect to it.
        if (!app.IsValidRedirectUri(redirect_uri)) return BadRequest("Invalid redirect_uri");

        if (response_type != "code")
            return RedirectError(redirect_uri, "unsupported_response_type", state);

        // PKCE validation.
        if (!string.IsNullOrEmpty(code_challenge) && code_challenge_method != "S256")
            return RedirectError(redirect_uri, "invalid_request", state, "only S256 PKCE is supported");
        bool pkceRequired = app.RequirePkce
            || (app.IsPublicClient && oidcSettings.Value.RequirePkceForPublicClients);
        if (pkceRequired && string.IsNullOrEmpty(code_challenge))
            return RedirectError(redirect_uri, "invalid_request", state, "PKCE required for this client");

        OidcAuthorizationRequest request = new() {
            ClientId            = client_id,
            RedirectUri         = redirect_uri,
            Scope               = scope ?? "",
            State               = state,
            Nonce               = nonce,
            CodeChallenge       = code_challenge,
            CodeChallengeMethod = code_challenge_method
        };
        string sessionId = OidcCrypto.NewHandle();
        cache.Set(SessionKey(sessionId), request, SessionExpiry);

        // Hand off to the consent UI; it only ever sees the opaque session id.
        string consentUrl = QueryHelpers.AddQueryString(
            apiSettings.Value.WebsiteUrl.TrimEnd('/') + "/oauth/authorize",
            "session", sessionId);
        return Redirect(consentUrl);
    }

    public class ConsentInfoResponse {
        public string ClientId { get; set; } = "";
        public string AppName { get; set; } = "";
        public string AppDescription { get; set; } = "";
        public string[] Scopes { get; set; } = [];
        public bool Denied { get; set; }
        public string? Redirect { get; set; }
    }

    /// <summary>Consent-screen data for a stored session. Also enforces the access gate so a
    /// blocked user is never shown a consent screen.</summary>
    [HttpGet("session/{sessionId}")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<ConsentInfoResponse>> GetSession(string sessionId) {
        if (!cache.TryGetValue(SessionKey(sessionId), out OidcAuthorizationRequest? request) || request == null)
            return NotFound("Authorization session not found or expired");

        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(request.ClientId);
        if (app == null) return BadRequest("Invalid client_id");

        AccessDecision decision = await accessPolicy.Evaluate(user, app);
        if (!decision.Allowed) {
            logger.LogInformation("OIDC access denied for user {UserId} on app {AppId}: {Reason}",
                user.Id, app.Id, decision.Reason);
            return Ok(new ConsentInfoResponse {
                ClientId = app.Id,
                AppName  = app.Name,
                Denied   = true,
                Redirect = BuildRedirect(request.RedirectUri,
                    new() { ["error"] = "access_denied" }, request.State)
            });
        }

        return Ok(new ConsentInfoResponse {
            ClientId       = app.Id,
            AppName        = app.Name,
            AppDescription = app.Description,
            Scopes         = OidcScopes.Parse(request.Scope)
        });
    }

    public class RedirectResponse {
        public string Redirect { get; set; } = "";
    }

    [HttpPost("session/{sessionId}/approve")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<RedirectResponse>> Approve(string sessionId) {
        if (!cache.TryGetValue(SessionKey(sessionId), out OidcAuthorizationRequest? request) || request == null)
            return NotFound("Authorization session not found or expired");

        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(request.ClientId);
        if (app == null) return BadRequest("Invalid client_id");

        // Re-check the gate at the moment of consent (defence in depth).
        AccessDecision decision = await accessPolicy.Evaluate(user, app);
        if (!decision.Allowed) {
            logger.LogInformation("OIDC access denied at approve for user {UserId} on app {AppId}: {Reason}",
                user.Id, app.Id, decision.Reason);
            cache.Remove(SessionKey(sessionId));
            return Ok(new RedirectResponse {
                Redirect = BuildRedirect(request.RedirectUri,
                    new() { ["error"] = "access_denied" }, request.State)
            });
        }

        OidcAuthorizationCode code = new() {
            Code                = OidcCrypto.NewHandle(),
            ClientId            = app.Id,
            UserId              = user.Id,
            RedirectUri         = request.RedirectUri,
            Scopes              = request.Scope,
            Nonce               = request.Nonce,
            CodeChallenge       = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod,
            AuthTimeUnix        = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        long expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            + oidcSettings.Value.AuthorizationCodeLifetimeSeconds;
        await codeRepo.StoreCode(code, expiresAt);
        cache.Remove(SessionKey(sessionId));

        logger.LogInformation("OIDC code issued for user {UserId} on app {AppId}", user.Id, app.Id);
        return Ok(new RedirectResponse {
            Redirect = BuildRedirect(request.RedirectUri,
                new() { ["code"] = code.Code }, request.State)
        });
    }

    [HttpPost("session/{sessionId}/deny")]
    [Authorize(Policy = "UserOnly")]
    public ActionResult<RedirectResponse> Deny(string sessionId) {
        if (!cache.TryGetValue(SessionKey(sessionId), out OidcAuthorizationRequest? request) || request == null)
            return NotFound("Authorization session not found or expired");
        cache.Remove(SessionKey(sessionId));
        return Ok(new RedirectResponse {
            Redirect = BuildRedirect(request.RedirectUri,
                new() { ["error"] = "access_denied" }, request.State)
        });
    }

    private IActionResult RedirectError(string redirectUri, string error, string? state, string? description = null) {
        Dictionary<string, string?> parameters = new() { ["error"] = error };
        if (description != null) parameters["error_description"] = description;
        return Redirect(BuildRedirect(redirectUri, parameters, state));
    }

    private static string BuildRedirect(string redirectUri, Dictionary<string, string?> parameters, string? state) {
        if (!string.IsNullOrEmpty(state)) parameters["state"] = state;
        return QueryHelpers.AddQueryString(redirectUri, parameters);
    }
}
