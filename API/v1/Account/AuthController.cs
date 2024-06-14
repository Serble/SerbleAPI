using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account; 

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IFido2 fido): ControllerManager {

    [HttpGet("")]  // Keep for backwards compatibility
    [HttpPost("password")]
    public IActionResult PasswordAuth([FromHeader] BasicAuthorizationHeader authorizationHeader) {
        if (authorizationHeader.IsNull()) {
            return BadRequest("Authorization header is missing");
        }
        if (!authorizationHeader.IsValid()) {
            return BadRequest("Authorization header is invalid");
        }
        
        // Valid header, check credentials
        string username = authorizationHeader.GetUsername();
        string password = authorizationHeader.GetPassword();
        if (password.Length > 256) {
            return BadRequest("Password cannot be longer than 256 characters");
        }
        Program.StorageService!.GetUserFromName(username, out User? user);
        if (user == null) {
            return Unauthorized();
        }
        if (!user.CheckPassword(password)) {
            return Unauthorized();
        }

        if (user.TotpEnabled) {
            // 2FA is enabled, return a first stage login token
            string mfaToken = TokenHandler.GenerateFirstStepLoginToken(user.Id);
            return Ok(new {
                mfa_token = mfaToken,
                success = true,
                mfa_required = true
            });
        }
        
        // Valid credentials, return token
        string token = TokenHandler.GenerateLoginToken(user.Id);
        return Ok(new {
            token,
            success = true,
            mfa_required = false
        });
    }

    [HttpPost("passkey/assertion")]  // Make Assertion
    public async Task<IActionResult> PasskeyAuth([FromBody] AuthenticatorAssertionRawResponse clientResponse,
        CancellationToken cancellationToken) {
        try {
            // Get the assertion options we sent the client
            string? jsonOptions = HttpContext.Session.GetString("fido2.assertionOptions");
            AssertionOptions? options = AssertionOptions.FromJson(jsonOptions);

            // Get registered credential from database
            Program.StorageService!.GetPasskey(clientResponse.Id, out SavedPasskey? creds);
            if (creds == null) {
                return BadRequest("Unknown passkey");
            }

            // Create callback to check if the user handle owns the credentialId
            IsUserHandleOwnerOfCredentialIdAsync callback = static (args, _) => {
                Program.StorageService.GetUsersPasskeys(Encoding.UTF8.GetString(args.UserHandle), out SavedPasskey[] storedCreds);
                return Task.FromResult(storedCreds.Any(c => c.Descriptor!.Id.SequenceEqual(args.CredentialId)));  // TODO: Should this be c.CredentialId
            };

            // Make the assertion
            VerifyAssertionResult res = await fido.MakeAssertionAsync(clientResponse, options, creds.PublicKey!, creds.DevicePublicKeys!, creds.SignCount, callback, cancellationToken: cancellationToken);
            Program.StorageService.SetPasskeySignCount(res.CredentialId, (int) res.SignCount);

            if (res.DevicePublicKey is not null) {
                creds.DevicePublicKeys = creds.DevicePublicKeys!.Append(res.DevicePublicKey).ToArray();
            }

            return Json(res);
        }
        catch (Exception e) {
            return BadRequest("Failed");
        }
    }
    
    [HttpPost]
    [Route("/assertionOptions")]
    public ActionResult AssertionOptionsPost([FromForm] string username) {
        try {
            //var existingCredentials = new List<PublicKeyCredentialDescriptor>();

            // if (!string.IsNullOrEmpty(username)) {
            //     // 1. Get user from DB
            //     var user = DemoStorage.GetUser(username) ?? throw new ArgumentException("Username was not registered");
            //
            //     // 2. Get registered credentials from database
            //     existingCredentials = DemoStorage.GetCredentialsByUser(user).Select(c => c.Descriptor).ToList();
            // }

            AuthenticationExtensionsClientInputs exts = new() {
                Extensions = true,
                UserVerificationMethod = true,
                DevicePubKey = new AuthenticationExtensionsDevicePublicKeyInputs()
            };

            // 3. Create options
            UserVerificationRequirement uv = string.IsNullOrEmpty(userVerification) ? UserVerificationRequirement.Discouraged : userVerification.ToEnum<UserVerificationRequirement>();
            AssertionOptions options = fido.GetAssertionOptions(
                existingCredentials,
                uv,
                exts
            );

            // 4. Temporarily store options, session/in-memory cache/redis/db
            HttpContext.Session.SetString("fido2.assertionOptions", options.ToJson());

            // 5. Return options to client
            return Json(options);
        }

        catch (Exception e) {
            return Json(new AssertionOptions { Status = "error", ErrorMessage = FormatException(e) });
        }
    }
    
    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, OPTIONS");
        return Ok();
    }
    
}