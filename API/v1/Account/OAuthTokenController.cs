using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account; 

// ReSharper disable InconsistentNaming

[ApiController]
[Route("api/v1/oauth/token")]
public class OAuthTokenController(
    ILogger<OAuthTokenController> logger,
    ITokenService tokens,
    IAppRepository appRepo) : ControllerManager {

    [HttpPost("refresh")]
    public ActionResult<AccessTokenResponse> RequestTokens(
        [FromQuery] string code,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        logger.LogDebug("Validating oauth code: " + code);
        if (!tokens.ValidateAuthorizationToken(code, client_id, out User? user, out string scope, out string reason)) {
            return BadRequest("Invalid authorization code: " + reason);
        }
        OAuthApp? app = appRepo.GetOAuthApp(client_id);
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
            ExpiresIn = 87600,
            AccessToken = tokens.GenerateAccessToken(user!.Id, scope),
            RefreshToken = tokens.GenerateRefreshToken(user.Id, client_id, scope),
            TokenType = "bearer"
        });
    }
    
    [HttpPost("access")]
    public ActionResult<AccessTokenResponse> RequestAccess(
        [FromQuery] string refresh_token,
        [FromQuery] string client_id,
        [FromQuery] string client_secret,
        [FromQuery] string grant_type) {
        if (!tokens.ValidateRefreshToken(refresh_token, client_id, out User? user, out string scope)) {
            return BadRequest("Invalid authorization code");
        }
        OAuthApp? app = appRepo.GetOAuthApp(client_id);
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
            ExpiresIn = 1,
            AccessToken = tokens.GenerateAccessToken(user.Id, scope),
            RefreshToken = refresh_token,
            TokenType = "bearer"
        });
    }
}