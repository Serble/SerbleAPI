using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/account/mfa")]
public class MfaController : ControllerManager {

    [HttpPost]
    public IActionResult Authenticate([FromBody] MfaAuthBody body) {
        if (body.LoginToken == null) {
            return Unauthorized("Login token is missing");
        }
        if (!TokenHandler.ValidateFirstStepLoginToken(body.LoginToken, out User user)) {
            return Unauthorized("Invalid login token");
        }
        
        // Valid token, check TOTP code
        if (!user.ValidateTotp(body.TotpCode)) {
            return Unauthorized("Invalid TOTP code");
        }
        
        // Valid TOTP code, return token
        string token = TokenHandler.GenerateLoginToken(user.Id);
        return Ok(new {
            token,
            success = true
        });
    }
    
    [HttpPost]
    [Route("totp")]
    public IActionResult CheckTotp([FromHeader] SerbleAuthorizationHeader auth, [FromBody] MfaAuthBody body) {
        if (!auth.CheckAndGetInfo(out User user, out _, ScopeHandler.ScopesEnum.ManageAccount)) {
            return Unauthorized();
        }
        
        // Valid token, check TOTP code
        if (!user.ValidateTotp(body.TotpCode)) {
            return Ok(new {
                success = true,
                valid = false
            });
        }
        
        return Ok(new {
            success = true,
            valid = true
        });
    }
    
    [HttpGet("totp/qrcode")]
    public IActionResult GetTotpQrCode([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? authType, out string? msg,
                out User target)) return Unauthorized(msg);
        if (authType != SerbleAuthorizationHeaderType.User) {
            return Unauthorized("Authorization header must be a user");
        }
        
        // Valid user, generate a TOTP QR code
        byte[]? qrCode = target.GetTotpQrCode();
        if (qrCode == null) {
            return BadRequest("TOTP is not enabled.");
        }
        return File(qrCode, "image/png");
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("totp/qrcode")]
    public ActionResult OptionsTotpQrCode() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
    [HttpOptions("totp")]
    public ActionResult OptionsCheckTotp() {
        HttpContext.Response.Headers.Add("Allow", "POST, OPTIONS");
        return Ok();
    }
    
}