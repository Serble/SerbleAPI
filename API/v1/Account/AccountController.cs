using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

[ApiController]
[Route("api/v1/account/")]
[Authorize]
public class AccountController(
    ILogger<AccountController> logger,
    IOptions<EmailSettings> emailSettings,
    IAntiSpamService antiSpam,
    IUserRepository userRepo,
    IEmailConfirmationService emailConfirmation) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<SanitisedUser>> Get() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return new SanitisedUser(target, HttpContext.User.GetScopeString());
    }

    [HttpDelete]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult> Delete() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();

        await userRepo.DeleteUser(target.Id);

        if (!target.VerifiedEmail) return Ok();

        string body = EmailSchemasService.GetEmailSchema(EmailSchema.AccountDeleted, LocalisationHandler.LanguageOrDefault(target));
        body = body.Replace("{name}", target.Username);
        Email email = new(logger, emailSettings.Value,
            target.Email.ToSingleItemEnumerable().ToArray(),
            FromAddress.System, "Serble Account Deletion", body);
        email.SendNonBlocking();
        return Ok();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<SanitisedUser>> Register([FromBody] RegisterRequestBody requestBody, [FromHeader] AntiSpamHeader antiSpamHeader) {
        if (!await antiSpam.Check(antiSpamHeader, HttpContext))
            return BadRequest("Anti-spam check failed");
        if (requestBody.Password.Length > 256)
            return BadRequest("Password cannot be longer than 256 characters");

        if (await userRepo.GetUserFromName(requestBody.Username) != null)
            return Conflict("User already exists");

        string passwordSalt = SerbleUtils.RandomString(64);
        User newUser = new() {
            Username     = requestBody.Username,
            PasswordHash = (requestBody.Password + passwordSalt).Sha256Hash(),
            PasswordSalt = passwordSalt,
            PermLevel    = 1
        };
        newUser.WithRepos(userRepo);
        User user = await userRepo.AddUser(newUser);
        logger.LogDebug("User " + user.Username + " created");
        return Ok(new SanitisedUser(user, "1", true));
    }

    [HttpPatch]
    [Authorize(Policy = "Scope:ManageAccount")]
    public async Task<ActionResult<SanitisedUser>> EditAccount([FromBody] AccountEditRequest[] edits) {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();

        if (edits.Any(e => e.Field.ToLower() == "password") && !HttpContext.User.IsUser())
            return Forbid();

        Dictionary<string, string> t = LocalisationHandler.GetTranslations(
            LocalisationHandler.GetPreferredLanguageOrDefault(Request, target));
        string scopes = HttpContext.User.GetScopeString();

        string originalEmail = target.Email;
        User newUser = target;
        foreach (AccountEditRequest editRequest in edits) {
            try {
                newUser = await editRequest.ApplyChanges(target, userRepo);
            } catch (ArgumentException e) {
                return BadRequest(e.Message);
            }
        }

        logger.LogDebug("Email from " + originalEmail + " to " + newUser.Email);
        if (newUser.Email != originalEmail && !string.IsNullOrWhiteSpace(newUser.Email)) {
            newUser.VerifiedEmail = false;
            logger.LogDebug("Sending email verification");
            emailConfirmation.SendConfirmationEmail(newUser);

            if (!string.IsNullOrWhiteSpace(originalEmail)) {
                logger.LogDebug("Sending email change notification to " + originalEmail);
                string body = EmailSchemasService.GetEmailSchema(EmailSchema.EmailChanged, LocalisationHandler.LanguageOrDefault(target));
                body = body.Replace("{name}", target.Username)
                    .Replace("{new_email}", newUser.Email)
                    .Replace("{old_email}", originalEmail);
                Email email = new(logger, emailSettings.Value,
                    originalEmail.ToSingleItemEnumerable().ToArray(),
                    FromAddress.System, t["email-changed-subject"], body);
                email.SendNonBlocking();
            }
        }

        await userRepo.UpdateUser(newUser);
        return new SanitisedUser(target, scopes);
    }
}

// This is here because it is.
// Don't remove it.
// ReSharper disable once UnusedType.Global
public class Adam {
    public Adam() { throw new Exception("Adam"); }
}