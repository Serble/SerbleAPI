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
    public ActionResult<AuthorizedApp[]> GetAll() {
        User? target = HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        target.ObtainAuthorizedApps();
        return target.AuthorizedApps;
    }

    [HttpPost]
    [Authorize(Policy = "UserOnly")]
    public IActionResult AuthorizeApp([FromBody] AuthorizedApp app) {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        OAuthApp? appObj = appRepo.GetOAuthApp(app.AppId);
        if (appObj == null) return BadRequest("Invalid app");

        user.AuthorizeApp(new AuthorizedApp(app.AppId, new Scopes(app.Scopes).ScopesString));
        return Ok(tokens.GenerateAuthorizationToken(user.Id, app.AppId, app.Scopes));
    }

    [HttpDelete("{appId}")]
    [Authorize(Policy = "UserOnly")]
    public ActionResult DeAuthorizeApp(string appId) {
        User? user = HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        if (user.AuthorizedApps.All(a => a.AppId != appId))
            return BadRequest("App is not authorized");

        userRepo.DeleteAuthorizedApp(user.Id, appId);
        return Ok();
    }
}