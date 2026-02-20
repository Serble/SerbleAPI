namespace SerbleAPI.Config;

public class PasskeySettings {
    /// <summary>
    /// The ID of the relying party.
    /// This should be the clean domain name of the website, without any protocol or path. For example, "serble.net".
    /// </summary>
    public string RelyingPartyId { get; set; } = null!;
    public string RelyingPartyName { get; set; } = null!;
    public string[] AllowedOrigins { get; set; } = null!;
    public string ServerIconUrl { get; set; } = null!;
}
