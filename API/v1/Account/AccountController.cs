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
    IEmailConfirmationService emailConfirmation,
    IPasswordHasher hasher,
    ICredentialRepository credentials) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<SanitisedUser>> Get() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return await SanitisedUser.Create(target, HttpContext.User.GetScopeString());
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

    // Sends a confirmation email, so this is a way to make our domain send mail.
    [RateLimit(RateLimitTiers.Costly)]
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<SanitisedUser>> Register([FromBody] RegisterRequestBody requestBody, [FromHeader] AntiSpamHeader antiSpamHeader) {
        if (!await antiSpam.Check(antiSpamHeader, HttpContext))
            return BadRequest("Anti-spam check failed");
        if (requestBody.Password.Length > 256)
            return BadRequest("Password cannot be longer than 256 characters");
        if (!UsernameRules.TryValidate(requestBody.Username, out string? usernameError))
            return BadRequest(usernameError);

        if (await userRepo.GetUserFromName(requestBody.Username) != null)
            return Conflict("User already exists");

        string passwordHash = await hasher.Hash(requestBody.Password);
        User newUser = new() {
            Username  = requestBody.Username,
            PermLevel = 1
        };
        newUser.WithRepos(userRepo);
        User user;
        try {
            user = await credentials.InTransaction(async () => {
                User added = await userRepo.AddUser(newUser);
                await credentials.SetPassword(added.Id, passwordHash);
                await credentials.ReplaceFlows(added.Id, [CredentialTypes.Bit(CredentialType.Password)]);
                return added;
            });
        }
        catch (UsernameTakenException) {
            // A concurrent registration claimed the name between the check above and the insert.
            // The unique index caught it, so answer as the check would have.
            return Conflict("User already exists");
        }
        logger.LogDebug("User " + user.Username + " created");
        return Ok(await SanitisedUser.Create(user, "1", true));
    }

    [HttpPatch]
    [Authorize(Policy = "Scope:ManageAccount")]
    public async Task<ActionResult<SanitisedUser>> EditAccount([FromBody] AccountEditRequest[] edits) {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();

        Dictionary<string, string> t = LocalisationHandler.GetTranslations(
            LocalisationHandler.GetPreferredLanguageOrDefault(Request, target));
        string scopes = HttpContext.User.GetScopeString();

        string originalEmail = target.Email;
        User newUser = target;
        foreach (AccountEditRequest editRequest in edits) {
            try {
                newUser = await editRequest.ApplyChanges(newUser, userRepo);
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

        try {
            await userRepo.UpdateUser(newUser);
        }
        catch (UsernameTakenException) {
            // Someone else took the name between ApplyChanges' availability check and this save.
            return BadRequest("Username is already taken");
        }

        SanitisedUser result = await SanitisedUser.Create(newUser, scopes);
        return result;
    }
}

// This is here because it is.
// Don't remove it.
// ReSharper disable once UnusedType.Global
public class Adam {
    public Adam() { throw new Exception("Adam"); }
}