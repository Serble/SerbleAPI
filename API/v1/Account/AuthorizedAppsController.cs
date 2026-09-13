using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/authorizedApps")]
[Authorize]
public class AuthorizedAppsController(
    ILogger<AuthorizedAppsController> logger,
    IUserRepository userRepo,
    IAppRepository appRepo,
    IOidcRefreshRepository refreshRepo,
    ITokenService tokens) : ControllerManager {

    /// <summary>
    /// Every app the user has granted access to, from either authorization flow. Each entry names
    /// the flow and carries its scopes resolved to identifiers, so a caller need not know either
    /// notation.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "Scope:ManageAccount")]
    public async Task<ActionResult<AuthorizedApp[]>> GetAll() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        await target.ObtainAuthorizedApps();
        return await target.GetAuthorizedApps();
    }

    [HttpPost]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> AuthorizeApp([FromBody] AuthorizedApp app) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        OAuthApp? appObj = await appRepo.GetOAuthApp(app.AppId);
        if (appObj == null) return BadRequest("Invalid app");

        await user.AuthorizeApp(new AuthorizedApp(app.AppId, new Scopes(app.Scopes).ScopesString));
        return Ok(tokens.GenerateAuthorizationToken(user.Id, app.AppId, app.Scopes));
    }

    /// <summary>
    /// Withdraws the user's grant to an app, whichever flow granted it. The refresh chains go with
    /// the consent row: an OIDC client holding one rotates it indefinitely otherwise.
    /// </summary>
    [HttpDelete("{appId}")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult> DeAuthorizeApp(string appId) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        AuthorizedApp[] authedApps = await user.GetAuthorizedApps();
        if (authedApps.All(a => a.AppId != appId)) {
            return BadRequest("App is not authorized");
        }

        await userRepo.DeleteAuthorizedApp(user.Id, appId);
        int revokedChains = await refreshRepo.RevokeUserClientGrants(user.Id, appId);
        logger.LogInformation("User {UserId} de-authorized app {AppId} ({Chains} refresh grants revoked)",
            user.Id, appId, revokedChains);
        return Ok();
    }
}