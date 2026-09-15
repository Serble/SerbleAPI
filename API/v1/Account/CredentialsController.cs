using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/credentials")]
[Authorize(Policy = "UserOnly")]
[RateLimit(RateLimitTiers.Write)]
public class CredentialsController(
    ICredentialService service,
    IUserRepository userRepo,
    ITokenService tokens) : ControllerManager {

    public record PasswordBody(string Password);
    public record TotpBody(string? Name);
    public record CodeBody(string Code);
    public record RenameBody(string Name);
    public record FlowsBody(string[][] Flows);

    private string UserId => HttpContext.User.GetUserId()!;

    [HttpGet]
    public async Task<ActionResult<CredentialOverview>> List() => await service.GetOverview(UserId);

    [HttpPut("password")]
    [RequireReauth]
    public async Task<IActionResult> SetPassword([FromBody] PasswordBody body) {
        if (body.Password.Length > 256) return BadRequest("Password cannot be longer than 256 characters");
        return await Respond(await service.SetPassword(UserId, body.Password));
    }

    [HttpPost("totp")]
    [RequireReauth]
    public async Task<IActionResult> BeginTotp([FromBody] TotpBody body) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        TotpEnrolment enrolment = await service.BeginTotp(user, body.Name);
        return Ok(new {
            id         = enrolment.Id,
            secret     = enrolment.Secret,
            otpauthUri = enrolment.OtpAuthUri,
            qrPng      = Convert.ToBase64String(enrolment.QrPng)
        });
    }

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("totp/{id}/verify")]
    public async Task<IActionResult> VerifyTotp(string id, [FromBody] CodeBody body) =>
        await Respond(await service.VerifyTotp(UserId, id, body.Code));

    [HttpPatch("{id}")]
    public async Task<IActionResult> Rename(string id, [FromBody] RenameBody body) =>
        await Respond(await service.Rename(UserId, id, body.Name));

    [HttpDelete("{id}")]
    [RequireReauth]
    public async Task<IActionResult> Delete(string id) =>
        await Respond(await service.Delete(UserId, id));

    [HttpPut("/api/v1/account/login-flows")]
    [RequireReauth]
    public async Task<IActionResult> SetFlows([FromBody] FlowsBody body) {
        List<int> masks = [];
        foreach (string[] flow in body.Flows) {
            int mask = 0;
            foreach (string name in flow) {
                if (!CredentialTypes.TryParse(name, out CredentialType type)) {
                    return BadRequest(new { error = "invalid", message = $"Unknown sign-in method '{name}'" });
                }
                mask |= CredentialTypes.Bit(type);
            }
            masks.Add(mask);
        }
        return await Respond(await service.SetFlows(UserId, masks));
    }

    private async Task<IActionResult> Respond(CredentialChangeResult result) {
        switch (result.Status) {
            case CredentialChangeStatus.Ok:
                CredentialOverview overview = await service.GetOverview(UserId);
                ReplacementTokens.Apply(overview, HttpContext, tokens, UserId, result.RevokedAt);
                return Ok(overview);
            case CredentialChangeStatus.NotFound:
                return NotFound(new { error = "not_found" });
            case CredentialChangeStatus.Conflict:
                return Conflict(new { error = "no_way_to_sign_in", message = result.Error });
            case CredentialChangeStatus.RateLimited:
                return TooManyRequests(result.RetryAfter);
            default:
                return BadRequest(new { error = "invalid", message = result.Error });
        }
    }
}
