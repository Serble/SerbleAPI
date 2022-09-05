using GeneralPurposeLib;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/")]
public class AccountController : ControllerManager {
    
    [HttpGet]
    public ActionResult<SanitisedUser> Get([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        return new SanitisedUser(target, scopes);
    }
    
    [HttpDelete]
    public ActionResult Delete([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.Check(out string scopes, out SerbleAuthorizationHeaderType? _, out string msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Unauthorized();
        }

        IEnumerable<ScopeHandler.ScopesEnum> scopesListEnum = ScopeHandler.ScopesIdsToEnumArray(ScopeHandler.StringToListOfScopeIds(scopes));
        if (!scopesListEnum.Contains(ScopeHandler.ScopesEnum.FullAccess)) {
            Logger.Debug("No full access");
            return Unauthorized();
        }
        
        // Delete the user's account
        Program.StorageService!.DeleteUser(target.Id);
        
        if (target.Email == "") return Ok();
        
        // Send an email
        string body = EmailSchemasService.GetEmailSchema(EmailSchema.AccountDeleted);
        body = body.Replace("{name}", target.Username);
        Email email = new(
            target.Email.ToSingleItemEnumerable().ToArray(), 
            FromAddress.System, "Serble Account Deletion", 
            body);
        email.SendNonBlocking();  // Don't await so the thread can continue
        return Ok();
    }

    [HttpPost]
    public async Task<ActionResult<SanitisedUser>> Register([FromBody] RegisterRequestBody requestBody, [FromHeader] AntiSpamProtection antiSpam) {
        if (!await antiSpam.Check()) {
            return BadRequest("Anti-spam check failed");
        }
        
        Program.StorageService!.GetUserFromName(requestBody.Username, out User? existingUser);
        if (existingUser != null) {
            return Conflict("User already exists");
        }
        User newUser = new() {
            Username = requestBody.Username,
            PasswordHash = requestBody.Password.Sha256Hash(),
            PermLevel = 1,
            PermString = "0"
        };
        Program.StorageService.AddUser(newUser, out User user);
        Logger.Debug("User " + user.Username + " created");
        return Ok(new SanitisedUser(user, "1", true)); // Ignore authed apps to stop error
    }

    [HttpPatch]
    public Task<ActionResult<SanitisedUser>> EditAccount([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] AccountEditRequest[] edits) {
        if (!authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg, out User target)) {
            Logger.Debug("Check failed: " + msg);
            return Task.FromResult<ActionResult<SanitisedUser>>(Unauthorized());
        }

        string originalEmail = target.Email;
        User newUser = target;
        foreach (AccountEditRequest editRequest in edits) {
            if (!editRequest.TryApplyChanges(newUser, out User modUser, out string applyErrorMsg)) {
                return Task.FromResult<ActionResult<SanitisedUser>>(BadRequest(applyErrorMsg));
            }
            newUser = modUser;
        }
        
        // Check for email change so we can send a confirmation email
        Logger.Debug("Email from " + originalEmail + " to " + newUser.Email);
        if (newUser.Email != originalEmail && newUser.Email != "") {
            // Make sure the new email is not verified
            newUser.VerifiedEmail = false;
            Logger.Debug("Sending email verification");
            EmailConfirmationService.SendConfirmationEmail(newUser);
            
            // Send email to old email
            string body = EmailSchemasService.GetEmailSchema(EmailSchema.EmailChanged);
            body = body.Replace("{name}", target.Username);
            body = body.Replace("{newEmail}", newUser.Email);
            Email email = new(
                target.Email.ToSingleItemEnumerable().ToArray(), 
                FromAddress.System, "Serble Email Changed", 
                body);
            email.SendNonBlocking();  // Don't await so the thread can continue
        }
        
        Program.StorageService!.UpdateUser(newUser);

        return Task.FromResult<ActionResult<SanitisedUser>>(new SanitisedUser(target, scopes));
    }

    [HttpOptions]
    public ActionResult Options() {
        HttpContext.Response.Headers.Add("Allow", "GET, DELETE, POST, PATCH, OPTIONS");
        return Ok();
    }
    
}

public class Adam {
    public Adam() {
        Logger.Error("OH NO U UNLEASHED ADAM");
        throw new Exception("Adam");
    }
}