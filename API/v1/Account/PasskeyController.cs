using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/auth/passkey")]
public class PasskeyController(IFido2 fido) : ControllerManager {
    
    [HttpPost("create")]
    public Task<IActionResult> PasskeyAuth([FromHeader] SerbleAuthorizationHeader auth) {
        throw new NotImplementedException();  // Keep this func?
    }

    [HttpPost("credentialoptions")]
    public IActionResult MakeCredentialOptions(
        [FromHeader] SerbleAuthorizationHeader auth,
        [FromForm] string attType,
        [FromForm] string authType,
        [FromForm] string residentKey,
        [FromForm] string userVerification) {
        if (!auth.CheckAndGetInfo(out User? user, 
                out _, 
                ScopeHandler.ScopesEnum.FullAccess, 
                false)) {
            return Unauthorized();
        }
        
        // 3. Create options
        AuthenticatorSelection authenticatorSelection = new() {
            ResidentKey = residentKey.ToEnum<ResidentKeyRequirement>(),
            UserVerification = userVerification.ToEnum<UserVerificationRequirement>()
        };

        if (!string.IsNullOrEmpty(authType)) {
            authenticatorSelection.AuthenticatorAttachment = authType.ToEnum<AuthenticatorAttachment>();
        }

        AuthenticationExtensionsClientInputs exts = new() {
            Extensions = true,
            UserVerificationMethod = true,
            DevicePubKey = new AuthenticationExtensionsDevicePublicKeyInputs() { Attestation = attType },
            CredProps = true
        };

        Fido2User fidoUser = new() {
            Name = user.Username,
            DisplayName = user.Username,
            Id = Encoding.UTF8.GetBytes(user.Id)
        };

        IReadOnlyList<PublicKeyCredentialDescriptor> excludeCreds = [];

        CredentialCreateOptions options = fido.RequestNewCredential(fidoUser, excludeCreds, authenticatorSelection, attType.ToEnum<AttestationConveyancePreference>(), exts);

        // 4. Temporarily store options, session/in-memory cache/redis/db
        HttpContext.Session.SetString("fido2.attestationOptions", options.ToJson());

        // 5. return options to client
        return Json(options);
    }

    [HttpPost("credential")]
    public async Task<IActionResult> MakeCredentialOptions([FromBody] AuthenticatorAttestationRawResponse attestationResponse, CancellationToken cancellationToken) {
        try {
            // 1. get the options we sent the client
            string? jsonOptions = HttpContext.Session.GetString("fido2.attestationOptions");
            CredentialCreateOptions? options = CredentialCreateOptions.FromJson(jsonOptions);

            // 2. Create callback so that lib can verify credential id is unique to this user
            IsCredentialIdUniqueToUserAsyncDelegate callback = static (args, _) => {
                Program.StorageService!.GetUserIdFromPasskeyId(args.CredentialId, out string? userId);
                return Task.FromResult(userId == null);
            };

            // 2. Verify and make the credentials
            MakeNewCredentialResult success = await fido.MakeNewCredentialAsync(attestationResponse, options, callback, cancellationToken: cancellationToken);

            // 3. Store the credentials in db
            string userId = Encoding.UTF8.GetString(success.Result!.User.Id);
            SavedPasskey cred = new() {
                OwnerId = userId,
                Name = "Passkey " + Guid.NewGuid(),  // Give it a random now
                CredentialId = success.Result!.Id,
                PublicKey = success.Result.PublicKey,
                AaGuid = success.Result.AaGuid,
                AttestationClientDataJson = success.Result.AttestationClientDataJson,
                Descriptor = new PublicKeyCredentialDescriptor(success.Result.Id),
                SignCount = success.Result.SignCount,
                AttestationFormat = success.Result.AttestationFormat,
                Transports = success.Result.Transports,
                IsBackupEligible = success.Result.IsBackupEligible,
                IsBackedUp = success.Result.IsBackedUp,
                AttestationObject = success.Result.AttestationObject,
                DevicePublicKeys = [success.Result.DevicePublicKey]
            };
            
            Program.StorageService!.CreatePasskey(cred);
            return Json(success);
        }
        catch (Exception e) {
            return BadRequest("Failed to create credentials: " + e.Message);
        }
    }
}