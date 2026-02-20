namespace SerbleAPI.Config;

public class ApiSettings {
    public string BindUrl { get; set; } = null!;
    public string WebsiteUrl { get; set; } = null!;
    public bool AllowAntiSpamBypass { get; set; }
    public string LiveUrl { get; set; } = null!;  // where the API is publicly accessible, used for links in emails and such
    public Dictionary<string, string> Redirects { get; set; } = new();
}
