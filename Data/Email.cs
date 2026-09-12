using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

/// <summary>
/// One outgoing message and the SMTP conversation that delivers it.
/// <para>
/// MailKit rather than <c>System.Net.Mail.SmtpClient</c>, because the latter's <c>EnableSsl</c>
/// only means STARTTLS on a plaintext connection. It cannot do implicit TLS, which is the only
/// mode <c>smtp.mx.cloudflare.net</c> accepts.
/// </para>
/// </summary>
public class Email {

    public string[] To { get; }
    public string From { get; }
    public string Subject { get; set; }
    public string Body { get; set; }

    public EmailSettings Settings { get; }
    public ILogger Logger { get; }

    public Email(ILogger logger, EmailSettings settings, IEnumerable<User> to, FromAddress from = FromAddress.System, string subject = "", string body = "") {
        Settings = settings;
        Logger = logger;

        To = to.Select(usr => usr.Email).ToArray();
        From = FromAddressEnumToString(from);
        Subject = subject;
        Body = body;
    }

    public Email(ILogger logger, EmailSettings settings, string[] to, FromAddress from = FromAddress.System, string subject = "", string body = "") {
        Settings = settings;
        Logger = logger;

        To = to;
        From = FromAddressEnumToString(from);
        Subject = subject;
        Body = body;
    }

    private string FromAddressEnumToString(FromAddress address) {
        return address switch {
            FromAddress.System => Settings.Addresses.System,
            FromAddress.Newsletter => Settings.Addresses.Newsletter,
            _ => throw new InvalidEmailException("Invalid FromAddress")
        };
    }

    public void SendNonBlocking() {
        Task.Run(SendAsync);
    }

    public async Task SendAsync() {
        MimeMessage message;
        try {
            message = CollateMessage();
        }
        catch (Exception e) {
            Logger.LogError("Email could not be built: " + e);
            return;
        }

        using SmtpClient client = new() { Timeout = Settings.TimeoutSeconds * 1000 };
        try {
            await client.ConnectAsync(Settings.SmtpHost, Settings.SmtpPort, ResolveSecurity());
            if (!string.IsNullOrEmpty(Settings.SmtpUsername)) {
                await client.AuthenticateAsync(Settings.SmtpUsername, Settings.SmtpPassword);
            }
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception e) {
            Logger.LogError("Email failed to send: " + e);
        }
    }

    public void Send() {
        MimeMessage message;
        try {
            message = CollateMessage();
        }
        catch (Exception e) {
            Logger.LogError("Email could not be built: " + e);
            return;
        }

        using SmtpClient client = new() { Timeout = Settings.TimeoutSeconds * 1000 };
        try {
            client.Connect(Settings.SmtpHost, Settings.SmtpPort, ResolveSecurity());
            if (!string.IsNullOrEmpty(Settings.SmtpUsername)) {
                client.Authenticate(Settings.SmtpUsername, Settings.SmtpPassword);
            }
            client.Send(message);
            client.Disconnect(true);
        }
        catch (Exception e) {
            Logger.LogError("Email failed to send: " + e);
        }
    }

    /// <summary>
    /// Maps the configured mode onto MailKit's. Note that <see cref="SmtpSecurity.Auto"/> is
    /// resolved here rather than passed through as <see cref="SecureSocketOptions.Auto"/>:
    /// MailKit's own Auto continues unencrypted when the server offers nothing, which is a silent
    /// downgrade on a setting whose whole point is encryption.
    /// </summary>
    private SecureSocketOptions ResolveSecurity() {
        return Settings.Security switch {
            SmtpSecurity.None => SecureSocketOptions.None,
            SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurity.StartTlsWhenAvailable => SecureSocketOptions.StartTlsWhenAvailable,
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => Settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
        };
    }

    private MimeMessage CollateMessage() {
        if (To.Any(string.IsNullOrEmpty)) {
            throw new InvalidEmailException("To Email is not valid");
        }

        MimeMessage msg = new();
        msg.From.Add(new MailboxAddress("Serble", From));
        foreach (string toAdr in To) {
            msg.To.Add(MailboxAddress.Parse(toAdr));
        }
        msg.Subject = Subject;
        msg.Body = new BodyBuilder { HtmlBody = Body }.ToMessageBody();
        return msg;
    }

}

public enum FromAddress {
    System,
    Newsletter
}

public class InvalidEmailException(string message) : Exception(message);
