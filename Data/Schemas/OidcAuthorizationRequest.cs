namespace SerbleAPI.Data.Schemas;

/// <summary>
/// A validated, pre-consent OIDC authorization request held server-side between the initial
/// <c>/authorize</c> redirect and the user's consent decision. The frontend only ever receives
/// the opaque session id; every security-relevant value here was validated by the backend.
/// </summary>
public class OidcAuthorizationRequest {
    public string ClientId { get; set; } = null!;
    public string RedirectUri { get; set; } = null!;
    public string Scope { get; set; } = null!;
    public string? State { get; set; }
    public string? Nonce { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    /// <summary>The raw space-delimited <c>prompt</c> parameter; <c>consent</c> forces the consent screen.</summary>
    public string? Prompt { get; set; }
}
