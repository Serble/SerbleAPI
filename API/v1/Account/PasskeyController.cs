using SerbleAPI.Config;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/auth/passkey")]
[RateLimit(RateLimitTiers.Write)]
public class PasskeyController(
    IFido2 fido,
    IUserRepository userRepo,
    IPasskeyRepository passkeyRepo,
    ICredentialService credentials,
    ITokenService tokens,
    IMemoryCache cache) : ControllerManager {

    public record CredentialBody(
        string ChallengeId,
        AuthenticatorAttestationRawResponse Attestation,
        string? Name,
        bool SignInAlone = false);

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
            id               = k.UserCredentialId,
            name             = k.Name,
            credentialId     = Convert.ToBase64String(k.CredentialId!),
            isBackupEligible = k.IsBackupEligible,
            isBackedUp       = k.IsBackedUp
        }));
    }

    [HttpDelete("delete/{name}")]
    [Authorize(Policy = "UserOnly")]
    [RequireReauth]
    public async Task<IActionResult> DeletePasskey(string name) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(user.Id);
        SavedPasskey? target = keys.FirstOrDefault(k => k.Name == name);
        if (target == null) return NotFound("Passkey not found");

        CredentialChangeResult result = await credentials.Delete(user.Id, target.UserCredentialId);
        if (result.Status == CredentialChangeStatus.Conflict) return Conflict(result.Error);
        if (result.Status != CredentialChangeStatus.Ok) return NotFound("Passkey not found");

        CredentialOverview overview = await credentials.GetOverview(user.Id);
        ReplacementTokens.Apply(overview, HttpContext, tokens, user.Id, result.RevokedAt);
        return Ok(overview);
    }

    [HttpPatch("rename/{name}")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> RenamePasskey(string name, [FromForm] string newName) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(newName)) return BadRequest("New name cannot be empty");
        SavedPasskey[] keys = await passkeyRepo.GetUsersPasskeys(user.Id);
        SavedPasskey? target = keys.FirstOrDefault(k => k.Name == name);
        if (target == null) return NotFound("Passkey not found");
        if (keys.Any(k => k.Name == newName)) return Conflict("A passkey with that name already exists");
        CredentialChangeResult result = await credentials.Rename(user.Id, target.UserCredentialId, newName);
        if (result.Status == CredentialChangeStatus.Invalid) return BadRequest(result.Error);
        return Ok(new { success = true });
    }

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("credentialoptions")]
    [Authorize(Policy = "UserOnly")]
    [RequireReauth]
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

        // The client sends this ID back in the body of POST /credential.
        string challengeId = Guid.NewGuid().ToString("N");
        cache.Set($"fido2:attestation:{challengeId}", options.ToJson(), ChallengeExpiry);

        return Json(new { challengeId, options });
    }

    [RateLimit(RateLimitTiers.Auth)]
    [HttpPost("credential")]
    [Authorize(Policy = "UserOnly")]
    public async Task<IActionResult> MakeCredential([FromBody] CredentialBody body, CancellationToken cancellationToken) {
        string userId = HttpContext.User.GetUserId()!;
        if (body.Name is { Length: > 255 }) return BadRequest("Name cannot be longer than 255 characters");
        try {
            string cacheKey = $"fido2:attestation:{body.ChallengeId}";
            if (!cache.TryGetValue(cacheKey, out string? jsonOptions) || jsonOptions == null)
                return BadRequest("Challenge not found or expired. Request new credential options.");

            CredentialCreateOptions options = CredentialCreateOptions.FromJson(jsonOptions);
            // The options were issued to one account; only that account may complete them.
            if (Encoding.UTF8.GetString(options.User.Id) != userId)
                return BadRequest("Challenge not found or expired. Request new credential options.");

            // Consume the challenge — one-time use only.
            cache.Remove(cacheKey);

            IsCredentialIdUniqueToUserAsyncDelegate callback = async (args, _) => {
                string? existingOwner = await passkeyRepo.GetUserIdFromPasskeyId(args.CredentialId);
                return existingOwner == null;
            };

            MakeNewCredentialResult success = await fido.MakeNewCredentialAsync(
                body.Attestation, options, callback, cancellationToken: cancellationToken);

            SavedPasskey cred = new() {
                OwnerId                   = userId,
                Name                      = string.IsNullOrWhiteSpace(body.Name) ? "Passkey " + Guid.NewGuid() : body.Name.Trim(),
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
            if (body.SignInAlone) await credentials.AllowPasskeyAlone(userId);
            return Json(new {
                success      = true,
                id           = cred.UserCredentialId,
                credentialId = Convert.ToBase64String(cred.CredentialId!)
            });
        }
        catch (Exception e) {
            return BadRequest("Failed to create credentials: " + e.Message);
        }
    }
}