using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

/// <summary>
/// A single-use, server-side OIDC authorization code. The code itself is a high-entropy
/// random handle (not a JWT) so it can be atomically consumed and carries the bound
/// authorization-request data (redirect uri, nonce, PKCE challenge, scopes, auth_time).
/// </summary>
public class DbOidcAuthorizationCode {
    [Key]
    [StringLength(128)]
    public string Code { get; set; } = null!;

    [StringLength(64)]
    public string ClientId { get; set; } = null!;

    [StringLength(64)]
    public string UserId { get; set; } = null!;

    public string RedirectUri { get; set; } = null!;

    [StringLength(512)]
    public string Scopes { get; set; } = null!;

    [StringLength(256)]
    public string? Nonce { get; set; }

    [StringLength(256)]
    public string? CodeChallenge { get; set; }

    [StringLength(16)]
    public string? CodeChallengeMethod { get; set; }

    public long AuthTimeUnix { get; set; }

    public long ExpiresAtUnix { get; set; }

    public bool Consumed { get; set; }
}
