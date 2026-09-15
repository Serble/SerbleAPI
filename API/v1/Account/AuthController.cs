using SerbleAPI.Config;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

/// <summary>The original sign-in endpoints, run on top of <see cref="ILoginSessionService"/>.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[RateLimit(RateLimitTiers.Auth)]
public class AuthController(ILoginSessionService login) : ControllerManager {

    [HttpGet("")]
    [HttpPost("password")]
    public async Task<IActionResult> PasswordAuth([FromHeader] BasicAuthorizationHeader authorizationHeader,
        CancellationToken cancellationToken) {
        if (authorizationHeader.IsNull()) return BadRequest("Authorization header is missing");
        if (!authorizationHeader.IsValid()) return BadRequest("Authorization header is invalid");

        string username = authorizationHeader.GetUsername();
        string password = authorizationHeader.GetPassword();
        if (password.Length > 256) return BadRequest("Password cannot be longer than 256 characters");

        LoginStarted? started = await login.StartLogin(username);
        if (started == null) return Unauthorized();

        LoginStepResult result = await login.Password(started.Handle, password, null, LoginPurpose.Login, cancellationToken);
        switch (result.Outcome) {
            case LoginStepOutcome.Complete:
                return Ok(new { token = result.Token, success = true, mfa_required = false });
            case LoginStepOutcome.Continue when (result.Methods & CredentialTypes.Bit(CredentialType.Totp)) != 0:
                return Ok(new { mfa_token = result.Handle, success = true, mfa_required = true });
            case LoginStepOutcome.Continue:
                await login.Cancel(started.Handle);
                return Unauthorized("This account requires a sign-in method this endpoint does not support");
            case LoginStepOutcome.RateLimited:
                return TooManyRequests(result.RetryAfter, "Too many attempts. Try again later.");
            default:
                await login.Cancel(started.Handle);
                return Unauthorized();
        }
    }

    [HttpPost("passkey/assertion")]
    public async Task<IActionResult> PasskeyAuth(
        [FromBody] AuthenticatorAssertionRawResponse clientResponse,
        [FromQuery] string challengeId,
        CancellationToken cancellationToken) {
        LoginStepResult result = await login.Passkey(challengeId, clientResponse, null, LoginPurpose.Login, cancellationToken);
        return result.Outcome switch {
            LoginStepOutcome.Complete        => Ok(new { token = result.Token, success = true }),
            LoginStepOutcome.Continue        => Unauthorized("Additional verification required"),
            LoginStepOutcome.WrongCredential => BadRequest("Passkey assertion failed"),
            LoginStepOutcome.RateLimited     => TooManyRequests(result.RetryAfter, "Too many attempts. Try again later."),
            _ => BadRequest("Challenge not found or expired. Request new assertion options.")
        };
    }

    [HttpGet("passkey/assertionOptions")]
    public Task<IActionResult> AssertionOptionsGet() => AssertionOptionsPost(null);

    [HttpPost("passkey/assertionOptions")]
    public async Task<IActionResult> AssertionOptionsPost([FromForm] string? username) {
        string? handle = null;
        if (!string.IsNullOrEmpty(username)) {
            LoginStarted? started = await login.StartLogin(username);
            if (started == null) return BadRequest("Invalid user");
            handle = started.Handle;
        }

        PasskeyOptionsResult? result = await login.PasskeyOptions(handle, null, LoginPurpose.Login);
        if (result == null) return BadRequest("A passkey cannot be used to sign in to this account");
        return Json(new { challengeId = result.Handle, options = result.Options });
    }
}
