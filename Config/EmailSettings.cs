namespace SerbleAPI.Config;

public class EmailSettings {
    public string SmtpHost { get; set; } = null!;
    public int SmtpPort { get; set; }
    public string SmtpUsername { get; set; } = null!;
    public string SmtpPassword { get; set; } = null!;
    
    public ApiEmailAddresses Addresses { get; set; } = null!;
}
