using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

/// <summary>
/// A server-side OIDC refresh-token grant. Only a hash of the refresh token is stored so a
/// database leak cannot reveal usable tokens. Tokens are rotated on each use: redeeming a
/// token marks it <see cref="Rotated"/> and issues a fresh one; presenting an already-rotated
/// or revoked token is treated as reuse and revokes the whole grant chain.
/// </summary>
public class DbOidcRefreshGrant {
    [Key]
    [StringLength(128)]
    public string TokenHash { get; set; } = null!;

    [StringLength(64)]
    public string GrantId { get; set; } = null!;

    [StringLength(64)]
    public string ClientId { get; set; } = null!;

    [StringLength(64)]
    public string UserId { get; set; } = null!;

    [StringLength(512)]
    public string Scopes { get; set; } = null!;

    public long AuthTimeUnix { get; set; }

    public long ExpiresAtUnix { get; set; }

    public bool Rotated { get; set; }

    public bool Revoked { get; set; }
}
