using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public class AuthController(
    IFido2 fido,
    ILogger<AuthController> logger,
    ITokenService tokens,
    IUserRepository userRepo,
    IPasskeyRepository passkeyRepo,
    IMemoryCache cache) : ControllerManager {

    private static readonly MemoryCacheEntryOptions ChallengeExpiry =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

    [HttpGet("")]
    [HttpPost("password")]
    public async Task<IActionResult> PasswordAuth([FromHeader] BasicAuthorizationHeader authorizationHeader) {
        if (authorizationHeader.IsNull()) return BadRequest("Authorization header is missing");
        if (!authorizationHeader.IsValid()) return BadRequest("Authorization header is invalid");

        string username = authorizationHeader.GetUsername();
        string password = authorizationHeader.GetPassword();
        if (password.Length > 256) return BadRequest("Password cannot be longer than 256 characters");

        User? user = await userRepo.GetUserFromName(username);
        if (user == null) return Unauthorized();
        if (!user.CheckPassword(password)) return Unauthorized();

        if (user.TotpEnabled) {
            string mfaToken = tokens.GenerateFirstStepLoginToken(user.Id);
            return Ok(new { mfa_token = mfaToken, success = true, mfa_required = true });
        }

        string token = tokens.GenerateLoginToken(user.Id);
        return Ok(new { token, success = true, mfa_required = false });
    }

    [HttpPost("passkey/assertion")]
    public async Task<IActionResult> PasskeyAuth(
        [FromBody] AuthenticatorAssertionRawResponse clientResponse,
        [FromQuery] string challengeId,
        CancellationToken cancellationToken) {
        try {
            string cacheKey = $"fido2:assertion:{challengeId}";
            if (!cache.TryGetValue(cacheKey, out string? jsonOptions) || jsonOptions == null)
                return BadRequest("Challenge not found or expired. Request new assertion options.");

            // Consume the challenge — one-time use only.
            cache.Remove(cacheKey);

            AssertionOptions? options = AssertionOptions.FromJson(jsonOptions);

            SavedPasskey? creds = await passkeyRepo.GetPasskey(clientResponse.Id);
            if (creds == null) return BadRequest("Unknown passkey");

            IsUserHandleOwnerOfCredentialIdAsync callback = (args, _) => 
                passkeyRepo.GetUsersPasskeys(Encoding.UTF8.GetString(args.UserHandle))
                .ContinueWith(t => t.Result.Any(c => c.Descriptor!.Id.SequenceEqual(args.CredentialId)), 
                    cancellationToken);

            VerifyAssertionResult res = await fido.MakeAssertionAsync(
                clientResponse, options,
                creds.PublicKey!, creds.DevicePublicKeys ?? [], creds.SignCount,
                callback, cancellationToken: cancellationToken);

            await passkeyRepo.SetPasskeySignCount(res.CredentialId, (int)res.SignCount);

            if (res.DevicePublicKey is not null) {
                byte[][] updatedKeys = (creds.DevicePublicKeys ?? []).Append(res.DevicePublicKey).ToArray();
                await passkeyRepo.UpdatePasskeyDevicePublicKeys(res.CredentialId, updatedKeys);
            }

            string token = tokens.GenerateLoginToken(creds.OwnerId!);
            return Ok(new { token, success = true });
        }
        catch (Exception e) {
            logger.LogDebug("Passkey assertion failed: " + e.Message);
            return BadRequest("Passkey assertion failed: " + e.Message);
        }
    }

    [HttpGet("passkey/assertionOptions")]
    public Task<ActionResult> AssertionOptionsGet() => AssertionOptionsPost(null);

    [HttpPost("passkey/assertionOptions")]
    public async Task<ActionResult> AssertionOptionsPost([FromForm] string? username) {
        try {
            List<PublicKeyCredentialDescriptor> existingCredentials = [];

            if (!string.IsNullOrEmpty(username)) {
                User? user = await userRepo.GetUserFromName(username);
                if (user == null) throw new Exception("Invalid user");
                existingCredentials = (await passkeyRepo.GetUsersPasskeys(user.Id))
                    .Select(k => k.Descriptor!)
                    .ToList();
            }

            AuthenticationExtensionsClientInputs exts = new() {
                Extensions             = true,
                UserVerificationMethod = true,
                DevicePubKey           = new AuthenticationExtensionsDevicePublicKeyInputs()
            };

            AssertionOptions options = fido.GetAssertionOptions(
                existingCredentials, UserVerificationRequirement.Required, exts);

            // Store assertion challenge in cache, return ID to client.
            string challengeId = Guid.NewGuid().ToString("N");
            cache.Set($"fido2:assertion:{challengeId}", options.ToJson(), ChallengeExpiry);

            return Json(new {
                challengeId,
                options
            });
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }
    }
}