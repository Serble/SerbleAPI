using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/mfa")]
public class MfaController(ITokenService tokens, IUserRepository userRepo) : ControllerManager {

    // Second step of the MFA login flow — no session token yet, only the first-step token from body
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Authenticate([FromBody] MfaAuthBody body) {
        if (body.LoginToken == null) {
            return Unauthorized("Login token is missing");
        }

        if (!tokens.ValidateFirstStepLoginToken(body.LoginToken, out string? userId)) {
            return Unauthorized("Invalid login token");
        }
        
        User? user = await userRepo.GetUser(userId!);
        if (user == null) {
            return Unauthorized("User not found");
        }

        if (!await user.ValidateTotp(body.TotpCode)) {
            return Unauthorized("Invalid TOTP code");
        }

        string token = tokens.GenerateLoginToken(user.Id);
        return Ok(new {
            token,
            success = true
        });
    }

    [HttpPost("totp")]
    [Authorize(Policy = "Scope:ManageAccount")]
    public async Task<IActionResult> CheckTotp([FromBody] MfaAuthBody body) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        bool valid = await user.ValidateTotp(body.TotpCode);
        return Ok(new {
            success = true,
            valid
        });
    }

    [HttpGet("totp/qrcode")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> GetTotpQrCode() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        byte[]? qrCode = await target.GetTotpQrCode();
        if (qrCode == null) return BadRequest("TOTP is not enabled.");
        return File(qrCode, "image/png");
    }
}