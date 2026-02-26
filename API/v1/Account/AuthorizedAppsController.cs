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
    IUserRepository userRepo,
    IAppRepository appRepo,
    ITokenService tokens) : ControllerManager {

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
        return Ok();
    }
}