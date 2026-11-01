using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// The user's side of cross-app sign-in: the consent page's data and decision, and the list of
/// consents on the authorized-apps page. The server-to-server half lives on the OIDC token endpoint.
///
/// <para>A caller that gets <c>consent_required</c> sends the user to the website's
/// <c>/oauth/cross-app?client_id=…&amp;target=…&amp;redirect_uri=…&amp;state=…</c>, which drives these
/// routes. <c>redirect_uri</c> must be one the caller registered, exactly as in a normal sign-in,
/// because the user is sent there afterwards.</para>
/// </summary>
[ApiController]
[Route("api/v1/account/crossApp")]
[Authorize]
public class CrossAppController(
    ILogger<CrossAppController> logger,
    IUserRepository userRepo,
    IAppRepository appRepo,
    IAppAccessPolicyService accessPolicy,
    ICrossAppGrantRepository crossAppGrants) : ControllerManager {

    public class CrossAppRequestInfo {
        public string CallerId { get; set; } = "";
        public string CallerName { get; set; } = "";
        public string CallerDescription { get; set; } = "";
        public bool CallerIsOfficial { get; set; }
        public string TargetId { get; set; } = "";
        public string TargetName { get; set; } = "";
        public string TargetDescription { get; set; } = "";
        public bool TargetIsOfficial { get; set; }
        /// <summary>The Serble permissions (legacy scope ids) the target receives.</summary>
        public string[] Scopes { get; set; } = [];
        public bool AlreadyGranted { get; set; }
        /// <summary>Nothing to ask: the frontend should approve straight away.</summary>
        public bool AutoApprove { get; set; }
        /// <summary>The pair can't be approved; send the user to <see cref="Redirect"/>.</summary>
        public bool Denied { get; set; }
        public string? Redirect { get; set; }
    }

    public class CrossAppDecisionBody {
        public string ClientId { get; set; } = "";
        public string Target { get; set; } = "";
        public string RedirectUri { get; set; } = "";
        public string? State { get; set; }
    }

    public class RedirectResponse {
        public string Redirect { get; set; } = "";
    }

    public class CrossAppGrantView {
        public string CallerAppId { get; set; } = "";
        public string TargetAppId { get; set; } = "";
        public DateTime DateCreated { get; set; }
    }

    /// <summary>The consent page's data. Also tells the page when there is nothing to ask.</summary>
    [HttpGet("request")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<CrossAppRequestInfo>> GetRequest(
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery] string? target,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? state) {
        (Pair? pair, ActionResult? error) = await LoadPair(clientId, target, redirectUri);
        if (error != null) return error;

        bool consented = await crossAppGrants.HasGrant(pair!.User.Id, pair.Caller.Id, pair.Target.Id);
        CrossAppDecision decision = await Decide(pair, consented);

        return Ok(new CrossAppRequestInfo {
            CallerId          = pair.Caller.Id,
            CallerName        = pair.Caller.Name,
            CallerDescription = pair.Caller.Description,
            CallerIsOfficial  = pair.Caller.IsOfficial,
            TargetId          = pair.Target.Id,
            TargetName        = pair.Target.Name,
            TargetDescription = pair.Target.Description,
            TargetIsOfficial  = pair.Target.IsOfficial,
            Scopes            = CrossAppRules.EffectiveScopes(pair.Target),
            AlreadyGranted    = consented,
            AutoApprove       = decision == CrossAppDecision.Allowed,
            Denied            = decision == CrossAppDecision.Forbidden,
            Redirect          = decision == CrossAppDecision.Forbidden
                ? BuildRedirect(redirectUri!, new() { ["error"] = "access_denied" }, state)
                : null
        });
    }

    [HttpPost("approve")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<RedirectResponse>> Approve([FromBody] CrossAppDecisionBody body) {
        (Pair? pair, ActionResult? error) = await LoadPair(body.ClientId, body.Target, body.RedirectUri);
        if (error != null) return error;

        // Re-checked at the moment of consent: the settings or the user's access may have changed
        // since the page loaded.
        bool consented = await crossAppGrants.HasGrant(pair!.User.Id, pair.Caller.Id, pair.Target.Id);
        CrossAppDecision decision = await Decide(pair, consented);
        if (decision == CrossAppDecision.Forbidden) {
            return Ok(new RedirectResponse {
                Redirect = BuildRedirect(body.RedirectUri, new() { ["error"] = "access_denied" }, body.State)
            });
        }

        // An official pair never needs a record, and recording one the user was never shown would
        // outlive the apps' official status.
        if (decision == CrossAppDecision.ConsentRequired) {
            await crossAppGrants.AddGrant(pair.User.Id, pair.Caller.Id, pair.Target.Id);
            logger.LogInformation("User {UserId} allowed {CallerId} to sign them in to {TargetId}",
                pair.User.Id, pair.Caller.Id, pair.Target.Id);
        }

        return Ok(new RedirectResponse {
            Redirect = BuildRedirect(body.RedirectUri, new() { ["target"] = pair.Target.Id }, body.State)
        });
    }

    [HttpPost("deny")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<RedirectResponse>> Deny([FromBody] CrossAppDecisionBody body) {
        // Validated like approve, so this can't be used to bounce the user to an unregistered URI.
        (Pair? _, ActionResult? error) = await LoadPair(body.ClientId, body.Target, body.RedirectUri);
        if (error != null) return error;
        return Ok(new RedirectResponse {
            Redirect = BuildRedirect(body.RedirectUri, new() { ["error"] = "access_denied" }, body.State)
        });
    }

    /// <summary>Every app pair the user has allowed.</summary>
    [HttpGet]
    [Authorize(Policy = "Scope:ManageAccount")]
    public async Task<ActionResult<CrossAppGrantView[]>> GetAll() {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        CrossAppGrant[] grants = await crossAppGrants.GetGrants(user.Id);
        return grants.Select(g => new CrossAppGrantView {
            CallerAppId = g.CallerAppId,
            TargetAppId = g.TargetAppId,
            DateCreated = g.DateCreated
        }).ToArray();
    }

    /// <summary>
    /// Withdraws a consent. The target keeps the grant it already has (that's on the
    /// authorized-apps page under the target), but the caller can't obtain new codes for it.
    /// </summary>
    [HttpDelete("{callerAppId}/{targetAppId}")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> Revoke(string callerAppId, string targetAppId) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        if (!await crossAppGrants.HasGrant(user.Id, callerAppId, targetAppId))
            return BadRequest("No such cross-app consent");
        await crossAppGrants.DeleteGrant(user.Id, callerAppId, targetAppId);
        logger.LogInformation("User {UserId} withdrew cross-app consent from {CallerId} to {TargetId}",
            user.Id, callerAppId, targetAppId);
        return Ok();
    }

    private record Pair(User User, OAuthApp Caller, OAuthApp Target);

    private async Task<(Pair? pair, ActionResult? error)> LoadPair(string? callerId, string? targetId, string? redirectUri) {
        if (string.IsNullOrEmpty(callerId)) return (null, BadRequest("Missing client_id"));
        if (string.IsNullOrEmpty(targetId)) return (null, BadRequest("Missing target"));
        if (string.IsNullOrEmpty(redirectUri)) return (null, BadRequest("Missing redirect_uri"));

        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return (null, Unauthorized());
        OAuthApp? caller = await appRepo.GetOAuthApp(callerId);
        if (caller == null) return (null, BadRequest("Invalid client_id"));
        // Exact match against the caller's registered URIs before we ever send the user there.
        if (!caller.IsValidRedirectUri(redirectUri)) return (null, BadRequest("Invalid redirect_uri"));
        OAuthApp? target = await appRepo.GetOAuthApp(targetId);
        if (target == null || target.Id == caller.Id) return (null, BadRequest("Invalid target"));
        return (new Pair(user, caller, target), null);
    }

    /// <summary>
    /// The same decision the token endpoint makes, plus the conditions it would refuse a code on
    /// regardless of consent (a public client on either side, or a user the apps' access policies
    /// keep out), so the page never collects a consent that can't be used.
    /// </summary>
    private async Task<CrossAppDecision> Decide(Pair pair, bool consented) {
        if (pair.Caller.IsPublicClient || pair.Target.IsPublicClient) return CrossAppDecision.Forbidden;
        if (!(await accessPolicy.Evaluate(pair.User, pair.Caller)).Allowed) return CrossAppDecision.Forbidden;
        if (!(await accessPolicy.Evaluate(pair.User, pair.Target)).Allowed) return CrossAppDecision.Forbidden;
        return CrossAppRules.Decide(pair.Caller, pair.Target, consented);
    }

    private static string BuildRedirect(string redirectUri, Dictionary<string, string?> parameters, string? state) {
        if (!string.IsNullOrEmpty(state)) parameters["state"] = state;
        return QueryHelpers.AddQueryString(redirectUri, parameters);
    }
}
