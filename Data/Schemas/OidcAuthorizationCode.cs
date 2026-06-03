namespace SerbleAPI.Data.Schemas;

/// <summary>Domain view of a consumed OIDC authorization code.</summary>
public class OidcAuthorizationCode {
    public string Code { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string RedirectUri { get; set; } = null!;
    public string Scopes { get; set; } = null!;
    public string? Nonce { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public long AuthTimeUnix { get; set; }
}
