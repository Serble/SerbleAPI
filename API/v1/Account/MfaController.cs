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
    
}