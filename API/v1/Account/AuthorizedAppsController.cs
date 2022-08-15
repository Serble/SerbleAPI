using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/account/authorizedApps")]
public class AuthorizedAppsController : ControllerManager {
    
    [HttpGet]
    public IActionResult GetAll([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? _, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        return Ok(target.AuthorizedApps.ToJson());
    }

    [HttpPost]
    public IActionResult AuthorizeApp([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] AuthorizedApp app) {
        if (!authorizationHeader.Check(out string _, out SerbleAuthorizationHeaderType? authType, out string msg, out User user)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        if (authType != SerbleAuthorizationHeaderType.User) {
            // Not a user
            Logger.Debug("Not a user");
            return Forbid("Only users can access this endpoint");
        }
        
        Program.StorageService!.GetOAuthApp(app.AppId, out OAuthApp? appObj);
        if (appObj == null) {
            return BadRequest("Invalid app");
        }
        AuthorizedApp validatedApp = new(app.AppId, new Scopes(app.Scopes).ScopesString);
        user.AuthorizeApp(validatedApp);
        return Ok(TokenHandler.GenerateAuthorizationToken(user.Id, app.AppId, app.Scopes));
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, POST, OPTIONS");
        return Ok();
    }
    
}