using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/auth")]
public class AuthController: ControllerManager {

    [HttpGet]  // Keep for backwards compatibility
    [HttpGet("password")]
    public IActionResult PasswordAuth([FromHeader] BasicAuthorizationHeader authorizationHeader) {
        if (authorizationHeader.IsNull()) {
            return BadRequest("Authorization header is missing");
        }
        if (!authorizationHeader.IsValid()) {
            return BadRequest("Authorization header is invalid");
        }
        
        // Valid header, check credentials
        string username = authorizationHeader.GetUsername();
        string password = authorizationHeader.GetPassword();
        if (password.Length > 256) {
            return BadRequest("Password cannot be longer than 256 characters");
        }
        Program.StorageService!.GetUserFromName(username, out User? user);
        if (user == null) {
            return Unauthorized();
        }
        if (!user.CheckPassword(password)) {
            return Unauthorized();
        }

        if (user.TotpEnabled) {
            // 2FA is enabled, return a first stage login token
            string mfaToken = TokenHandler.GenerateFirstStepLoginToken(user.Id);
            return Ok(new {
                mfa_token = mfaToken,
                success = true,
                mfa_required = true
            });
        }
        
        // Valid credentials, return token
        string token = TokenHandler.GenerateLoginToken(user.Id);
        return Ok(new {
            token,
            success = true,
            mfa_required = false
        });
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}