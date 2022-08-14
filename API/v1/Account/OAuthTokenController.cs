using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

// ReSharper disable InconsistentNaming

[ApiController]
[Route("api/v1/oauth/token")]
public class OAuthTokenController : ControllerManager {

    [HttpPost("refresh")]
    public ActionResult<AccessTokenResponse> RequestTokens(
        [FromQuery] string code,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        if (!TokenHandler.ValidateAuthorizationToken(code, client_id, out User? user, out string scope)) {
            return BadRequest("Invalid authorization code");
        }
        Program.StorageService!.GetOAuthApp(client_id, out OAuthApp? app);
        if (app == null) {
            return BadRequest("Invalid client_id");
        }
        if (app.ClientSecret != client_secret) {
            return BadRequest("Invalid client_secret");
        }
        if (grant_type != "authorization_code") {
            return BadRequest("Invalid grant_type, must be 'authorization_code'");
        }

        return Ok(new AccessTokenResponse {
            expires_in = 3600000,
            access_token = TokenHandler.GenerateAccessToken(user!.Id, scope),
            refresh_token = TokenHandler.GenerateRefreshToken(user.Id, client_id, scope),
            token_type = "bearer"
        });
    }
    
    [HttpPost("access")]
    public ActionResult<AccessTokenResponse> RequestAccess(
        [FromQuery] string refresh_token,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        if (!TokenHandler.ValidateRefreshToken(refresh_token, client_id, out User? user, out string scope)) {
            return BadRequest("Invalid authorization code");
        }
        Program.StorageService!.GetOAuthApp(client_id, out OAuthApp? app);
        if (app == null) {
            return BadRequest("Invalid client_id");
        }
        if (app.ClientSecret != client_secret) {
            return BadRequest("Invalid client_secret");
        }
        if (grant_type != "authorization_code") {
            return BadRequest("Invalid grant_type, must be 'authorization_code'");
        }
        if (user!.AuthorizedApps.All(authorizedApp => authorizedApp.AppId != client_id)) {
            return BadRequest("App is not authorized");
        }

        return Ok(new AccessTokenResponse {
            expires_in = 3153600000,
            access_token = TokenHandler.GenerateAccessToken(user.Id, scope),
            refresh_token = refresh_token,
            token_type = "bearer"
        });
    }
    
    [HttpOptions("access")]
    public ActionResult OptionsAcc() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("refresh")]
    public ActionResult OptionsRef() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
}