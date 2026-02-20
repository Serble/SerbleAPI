using System.Net;
using System.Net.Mail;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

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
        Task.Run(Send);
    }

    public async Task SendAsync() {
        try {
            await GenerateClient().SendMailAsync(CollateMessage());
        }
        catch (Exception e) {
            Logger.LogError("Email failed to send: " + e);
        }
    }

    public void Send() {
        try {
            GenerateClient().Send(CollateMessage());
        }
        catch (Exception e) {
            Logger.LogError("Email failed to send: " + e);
        }
    }

    private SmtpClient GenerateClient() {
        SmtpClient client = new (Settings.SmtpHost) {
            Port = Settings.SmtpPort,
            Credentials = new NetworkCredential(Settings.SmtpUsername, Settings.SmtpPassword),
            EnableSsl = true
        };
        return client;
    }

    private MailMessage CollateMessage() {
        if (To.Any(string.IsNullOrEmpty)) {
            throw new InvalidEmailException("To Email is not valid");
        }
        
        MailMessage msg = new();
        msg.From = new MailAddress(From, "Serble");
        foreach (string toAdr in To) {
            msg.To.Add(new MailAddress(toAdr));
        }
        msg.Subject = Subject;
        msg.Body = Body;
        msg.IsBodyHtml = true;
        return msg;
    }
    
}

public enum FromAddress {
    System,
    Newsletter
}

public class InvalidEmailException(string message) : Exception(message);
