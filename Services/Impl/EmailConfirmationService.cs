using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Impl; 

public class EmailConfirmationService(ILogger<EmailConfirmationService> logger, IOptions<EmailSettings> settings, 
    IOptions<ApiSettings> apiSettings, ITokenService tokens) : IEmailConfirmationService {

    public void SendConfirmationEmail(User user) {
        if (user.VerifiedEmail) {
            throw new Exception("User has already verified their email");
        }

        string body = EmailSchemasService.GetEmailSchema(EmailSchema.ConfirmationEmail, LocalisationHandler.LanguageOrDefault(user));
        body = body.Replace("{name}", user.Username);
        body = body.Replace(
            "{confirmation_link}", 
            apiSettings.Value.LiveUrl + "api/v1/emailconfirm?token=" + tokens.GenerateEmailConfirmationToken(user.Id, user.Email));

        Email confirmationEmail = new (logger, settings.Value, user.Email.ToSingleItemEnumerable().ToArray()) {
            Subject = "Serble Email Confirmation",
            Body = body
        };

        confirmationEmail.SendAsync().ContinueWith(_ => logger.LogDebug("Sent confirmation email to " + user.Email));
        logger.LogDebug("Sending confirmation email to " + user.Email);
    }
    
}