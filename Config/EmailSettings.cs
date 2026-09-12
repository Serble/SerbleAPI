namespace SerbleAPI.Config;

/// <summary>
/// How the transport layer is secured when connecting to the SMTP server.
/// </summary>
public enum SmtpSecurity {

    /// <summary>
    /// Pick from the port: 465 means <see cref="SslOnConnect"/>, anything else
    /// <see cref="StartTls"/>. Never falls back to an unencrypted session.
    /// </summary>
    Auto,

    /// <summary>No encryption. Only for a local test server.</summary>
    None,

    /// <summary>
    /// Connect in the clear and upgrade with STARTTLS, failing if the server does not offer it.
    /// The usual submission setup on port 587.
    /// </summary>
    StartTls,

    /// <summary>
    /// Upgrade with STARTTLS if offered, otherwise continue unencrypted. Weaker than
    /// <see cref="StartTls"/> because a downgrade is silent.
    /// </summary>
    StartTlsWhenAvailable,

    /// <summary>
    /// TLS from the first byte, with no plaintext phase. Port 465, and the only mode
    /// <c>smtp.mx.cloudflare.net</c> accepts.
    /// </summary>
    SslOnConnect
}

public class EmailSettings {
    public string SmtpHost { get; set; } = null!;
    public int SmtpPort { get; set; }
    public string SmtpUsername { get; set; } = null!;
    public string SmtpPassword { get; set; } = null!;

    /// <summary>
    /// Transport security. Left at <see cref="SmtpSecurity.Auto"/> this follows the port, which
    /// is right for both a 465 relay and a 587 submission server.
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    /// <summary>
    /// How long to wait on the connection before giving up. Sending happens off the request
    /// thread, so this only bounds how long a stuck send holds a thread pool slot.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    public ApiEmailAddresses Addresses { get; set; } = null!;
}
