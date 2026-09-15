using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Config;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/mfa")]
[RateLimit(RateLimitTiers.Auth)]
public class MfaController(ILoginSessionService login) : ControllerManager {

    /// <summary>The TOTP step of a sign-in started at <c>GET api/v1/auth</c>, which returned the login token.</summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Authenticate([FromBody] MfaAuthBody body) {
        if (body.LoginToken == null) return Unauthorized("Login token is missing");

        LoginStepResult result = await login.Totp(body.LoginToken, body.TotpCode, null, CurrentLoginClient, LoginPurpose.Login);
        return result.Outcome switch {
            LoginStepOutcome.Complete        => Ok(new { token = result.Token, success = true, device_token = result.DeviceToken }),
            LoginStepOutcome.Continue        => Unauthorized("Additional verification required"),
            LoginStepOutcome.WrongCredential => Unauthorized("Invalid TOTP code"),
            LoginStepOutcome.RateLimited     => TooManyRequests(result.RetryAfter, "Too many MFA attempts. Try again later."),
            _ => Unauthorized("Invalid login token")
        };
    }
}
