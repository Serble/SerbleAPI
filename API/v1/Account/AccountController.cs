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
        if (authorizationHeader.Check(out string? scopes, out SerbleAuthorizationHeaderType? _, out string? msg,
                out User target)) return new SanitisedUser(target, scopes);
        return Unauthorized();
    }
    
    [HttpDelete]
    public ActionResult Delete([FromHeader] SerbleAuthorizationHeader authorizationHeader) {
        if (!authorizationHeader.CheckAndGetInfo(out User target, out Dictionary<string, string> t, null, false, Request)) {
            return Unauthorized();
        }

        // Delete the user's account
        Program.StorageService!.DeleteUser(target.Id);
        
        if (!target.VerifiedEmail) return Ok();
        
        // Send an email
        string body = EmailSchemasService.GetEmailSchema(EmailSchema.AccountDeleted, LocalisationHandler.LanguageOrDefault(target));
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
        if (!await antiSpam.Check(HttpContext)) {
            return BadRequest("Anti-spam check failed");
        }
        if (requestBody.Password.Length > 256) {
            return BadRequest("Password cannot be longer than 256 characters");
        }
        
        Program.StorageService!.GetUserFromName(requestBody.Username, out User? existingUser);
        if (existingUser != null) {
            return Conflict("User already exists");
        }
        string passwordSalt = SerbleUtils.RandomString(64);
        User newUser = new() {
            Username = requestBody.Username,
            PasswordHash = (requestBody.Password + passwordSalt).Sha256Hash(),
            PasswordSalt = passwordSalt,
            PermLevel = 1,
            PermString = "0"
        };
        Program.StorageService.AddUser(newUser, out User user);
        Logger.Debug("User " + user.Username + " created");
        return Ok(new SanitisedUser(user, "1", true)); // Ignore authed apps to stop error
    }

    [HttpPatch]
    public Task<ActionResult<SanitisedUser>> EditAccount([FromHeader] SerbleAuthorizationHeader authorizationHeader, [FromBody] AccountEditRequest[] edits) {
        if (!authorizationHeader.CheckAndGetInfo(out User target, out Dictionary<string, string> t, out SerbleAuthorizationHeaderType type, out string scopes, ScopeHandler.ScopesEnum.ManageAccount, false, Request)) {
            return Task.FromResult<ActionResult<SanitisedUser>>(Unauthorized());
        }

        if (edits.Any(e => e.Field.ToLower() == "password") && type != SerbleAuthorizationHeaderType.User) {
            return Task.FromResult<ActionResult<SanitisedUser>>(Forbid());
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
            string body = EmailSchemasService.GetEmailSchema(EmailSchema.EmailChanged, LocalisationHandler.LanguageOrDefault(target));
            body = body.Replace("{name}", target.Username);
            body = body.Replace("{new_email}", newUser.Email);
            body = body.Replace("{old_email}", originalEmail);
            Email email = new(
                originalEmail.ToSingleItemEnumerable().ToArray(), 
                FromAddress.System, t["email-changed-subject"], 
                body);
            email.SendNonBlocking();  // Don't await so the thread can continue
        }
        
        Program.StorageService!.UpdateUser(newUser);

        return Task.FromResult<ActionResult<SanitisedUser>>(new SanitisedUser(target, scopes));
    }

    [HttpPost("requestinfo")]
    public async Task<ActionResult> RequestAccountData([FromHeader] SerbleAuthorizationHeader auth) {
        if (!auth.CheckAndGetInfo(out User target, out _, ScopeHandler.ScopesEnum.FullAccess, false, Request)) {
            return Unauthorized();
        }
        if (!target.VerifiedEmail) {
            return BadRequest("You must verify your email before requesting your data");
        }
        AccountDataHandler.ScheduleUserDataCollation(target);
        return Ok();
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