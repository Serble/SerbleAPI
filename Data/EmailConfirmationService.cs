using GeneralPurposeLib;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data; 

public static class EmailConfirmationService {

    public static void SendConfirmationEmail(User user) {
        if (user.VerifiedEmail) {
            throw new Exception("User has already verified their email");
        }

        string body = EmailSchemasService.GetEmailSchema(EmailSchema.ConfirmationEmail, LocalisationHandler.LanguageOrDefault(user));
        body = body.Replace("{name}", user.Username);
        body = body.Replace(
            "{confirmation_link}", 
            Program.Config!["my_host"] + "api/v1/emailconfirm?token=" + TokenHandler.GenerateEmailConfirmationToken(user.Id, user.Email));

        Email confirmationEmail = new (user.Email.ToSingleItemEnumerable().ToArray()) {
            Subject = "Serble Email Confirmation",
            Body = body
        };

        confirmationEmail.SendAsync().ContinueWith(_ => Logger.Debug("Sent confirmation email to " + user.Email));
        Logger.Debug("Sending confirmation email to " + user.Email);
    }
    
}