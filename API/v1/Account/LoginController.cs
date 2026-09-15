using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

/// <summary>
/// Sign-in and re-authentication. A session is started, then steps are completed until one of the
/// account's sign-in flows is satisfied. Session handles travel in request bodies only.
/// A completed session returns a <c>deviceToken</c>; sending it back in <see cref="ControllerManager.DeviceTokenHeader"/>
/// keeps the client's attempts apart from anyone else's, so others' failures cannot lock it out.
/// </summary>
[ApiController]
[Route("api/v1/auth/login")]
public class LoginController(ILoginSessionService login) : ControllerManager {

    public record StartBody(string Username);
    public record PasswordBody(string LoginSession, string Password);
    public record TotpBody(string LoginSession, string Code);
    public record PasskeyOptionsBody(string? LoginSession);
    public record PasskeyBody(string LoginSession, AuthenticatorAssertionRawResponse Assertion);
    public record SessionBody(string LoginSession);

    private string? CallerUserId => HttpContext.User.IsUser() ? HttpContext.User.GetUserId() : null;

    [RateLimit(RateLimitTiers.Write)]
    [HttpPost("start")]
    [AllowAnonymous]
    public async Task<IActionResult> Start([FromBody] StartBody body) {
        LoginStarted? started = await login.StartLogin(body.Username);
        if (started == null) return Unauthorized(new { error = "invalid_credentials" });
        return Ok(StartedView(started));
    }

    [RateLimit(RateLimitTiers.Write)]
    [HttpPost("/api/v1/account/reauth")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> StartReauth() =>
        Ok(StartedView(await login.StartReauth(HttpContext.User.GetUserId()!)));

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("password")]
    [AllowAnonymous]
    public async Task<IActionResult> Password([FromBody] PasswordBody body, CancellationToken cancellationToken) {
        if (body.Password.Length > 256) return BadRequest("Password cannot be longer than 256 characters");
        return StepResponse(await login.Password(body.LoginSession, body.Password, CallerUserId, CurrentLoginClient,
            cancellationToken: cancellationToken));
    }

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("totp")]
    [AllowAnonymous]
    public async Task<IActionResult> Totp([FromBody] TotpBody body) =>
        StepResponse(await login.Totp(body.LoginSession, body.Code, CallerUserId, CurrentLoginClient));

    [RateLimit(RateLimitTiers.Write)]
    [HttpPost("passkey/options")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyOptions([FromBody] PasskeyOptionsBody body) {
        PasskeyOptionsResult? result = await login.PasskeyOptions(body.LoginSession, CallerUserId);
        if (result == null) return BadRequest(new { error = "invalid_session" });
        return Json(new { loginSession = result.Handle, options = result.Options });
    }

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("passkey")]
    [AllowAnonymous]
    public async Task<IActionResult> Passkey([FromBody] PasskeyBody body, CancellationToken cancellationToken) =>
        StepResponse(await login.Passkey(body.LoginSession, body.Assertion, CallerUserId, cancellationToken: cancellationToken));

    [RateLimit(RateLimitTiers.Write)]
    [HttpPost("cancel")]
    [AllowAnonymous]
    public async Task<IActionResult> Cancel([FromBody] SessionBody body) {
        await login.Cancel(body.LoginSession);
        return Ok(new { success = true });
    }

    private static object StartedView(LoginStarted started) => new {
        loginSession = started.Handle,
        methods      = CredentialTypes.Names(started.Methods),
        expiresAt    = started.ExpiresAt
    };

    private IActionResult StepResponse(LoginStepResult result) => result.Outcome switch {
        LoginStepOutcome.Continue => Ok(new {
            complete     = false,
            loginSession = result.Handle,
            methods      = CredentialTypes.Names(result.Methods)
        }),
        LoginStepOutcome.Complete when result.Purpose == LoginPurpose.Reauth =>
            Ok(new { complete = true, reauthToken = result.Token, deviceToken = result.DeviceToken }),
        LoginStepOutcome.Complete => Ok(new { complete = true, token = result.Token, deviceToken = result.DeviceToken }),
        LoginStepOutcome.WrongCredential => Unauthorized(new {
            error        = "invalid_credentials",
            loginSession = result.Handle,
            methods      = CredentialTypes.Names(result.Methods)
        }),
        LoginStepOutcome.RateLimited => TooManyRequests(result.RetryAfter),
        _ => BadRequest(new { error = "invalid_session" })
    };
}
