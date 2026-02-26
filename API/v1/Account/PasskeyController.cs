using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/auth/passkey")]
public class PasskeyController(IFido2 fido, IUserRepository userRepo, IPasskeyRepository passkeyRepo, IMemoryCache cache) : ControllerManager {

    // Challenge entries expire after 5 minutes — enough time to complete the
    // browser interaction without leaving stale data in memory indefinitely.
    private static readonly MemoryCacheEntryOptions ChallengeExpiry =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

    [HttpGet("list")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> ListPasskeys() {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(user.Id);
        return Json(keys.Select(k => new {
            name             = k.Name,
            credentialId     = Convert.ToBase64String(k.CredentialId!),
            isBackupEligible = k.IsBackupEligible,
            isBackedUp       = k.IsBackedUp
        }));
    }

    [HttpDelete("delete/{name}")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> DeletePasskey(string name) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(user.Id);
        SavedPasskey? target = keys.FirstOrDefault(k => k.Name == name);
        if (target == null) return NotFound("Passkey not found");
        await passkeyRepo.DeletePasskey(target.CredentialId!);
        return Ok(new { success = true });
    }

    [HttpPost("credentialoptions")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> MakeCredentialOptions(
        [FromForm] string  attType,
        [FromForm] string? authType,
        [FromForm] string? userVerification) {

        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();

        AuthenticatorSelection authenticatorSelection = new() {
            ResidentKey      = ResidentKeyRequirement.Required,
            UserVerification = UserVerificationRequirement.Required
        };
        if (!string.IsNullOrEmpty(authType))
            authenticatorSelection.AuthenticatorAttachment = authType.ToEnum<AuthenticatorAttachment>();

        AuthenticationExtensionsClientInputs exts = new() {
            Extensions             = true,
            UserVerificationMethod = true,
            DevicePubKey           = new AuthenticationExtensionsDevicePublicKeyInputs { Attestation = attType },
            CredProps              = true
        };

        Fido2User fidoUser = new() {
            Name        = user.Username,
            DisplayName = user.Username,
            Id          = Encoding.UTF8.GetBytes(user.Id)
        };

        IReadOnlyList<PublicKeyCredentialDescriptor> excludeCreds = (await passkeyRepo
                .GetUsersPasskeys(user.Id))
            .Select(k => k.Descriptor!)
            .Where(d => d != null!)
            .ToList();

        CredentialCreateOptions options = fido.RequestNewCredential(
            fidoUser, excludeCreds, authenticatorSelection,
            attType.ToEnum<AttestationConveyancePreference>(), exts);

        // Store the challenge in the in-memory cache under a random ID and
        // return that ID to the client.  The client sends it back as a query
        // parameter on the follow-up POST /credential request.  This avoids
        // any reliance on session cookies, which are unreliable across origins.
        string challengeId = Guid.NewGuid().ToString("N");
        cache.Set($"fido2:attestation:{challengeId}", options.ToJson(), ChallengeExpiry);

        return Json(new { challengeId, options });
    }

    [HttpPost("credential")]
    [AllowAnonymous]
    public async Task<IActionResult> MakeCredential(
        [FromBody] AuthenticatorAttestationRawResponse attestationResponse,
        [FromQuery] string challengeId,
        CancellationToken cancellationToken) {
        try {
            string cacheKey = $"fido2:attestation:{challengeId}";
            if (!cache.TryGetValue(cacheKey, out string? jsonOptions) || jsonOptions == null)
                return BadRequest("Challenge not found or expired. Request new credential options.");

            // Consume the challenge — one-time use only.
            cache.Remove(cacheKey);

            CredentialCreateOptions? options = CredentialCreateOptions.FromJson(jsonOptions);

            IsCredentialIdUniqueToUserAsyncDelegate callback = async (args, _) => {
                string? userId = await passkeyRepo.GetUserIdFromPasskeyId(args.CredentialId);
                return userId == null;
            };

            MakeNewCredentialResult success = await fido.MakeNewCredentialAsync(
                attestationResponse, options, callback, cancellationToken: cancellationToken);

            string userId = Encoding.UTF8.GetString(success.Result!.User.Id);
            SavedPasskey cred = new() {
                OwnerId                   = userId,
                Name                      = "Passkey " + Guid.NewGuid(),
                CredentialId              = success.Result!.Id,
                PublicKey                 = success.Result.PublicKey,
                AaGuid                    = success.Result.AaGuid,
                AttestationClientDataJson = success.Result.AttestationClientDataJson,
                Descriptor                = new PublicKeyCredentialDescriptor(
                    PublicKeyCredentialType.PublicKey, success.Result.Id, success.Result.Transports),
                SignCount                 = success.Result.SignCount,
                AttestationFormat         = success.Result.AttestationFormat,
                Transports                = success.Result.Transports,
                IsBackupEligible          = success.Result.IsBackupEligible,
                IsBackedUp                = success.Result.IsBackedUp,
                AttestationObject         = success.Result.AttestationObject,
                DevicePublicKeys          = success.Result.DevicePublicKey != null
                    ? [success.Result.DevicePublicKey] : []
            };

            await passkeyRepo.CreatePasskey(cred);
            return Json(new { success = true, credentialId = Convert.ToBase64String(cred.CredentialId!) });
        }
        catch (Exception e) {
            return BadRequest("Failed to create credentials: " + e.Message);
        }
    }
}