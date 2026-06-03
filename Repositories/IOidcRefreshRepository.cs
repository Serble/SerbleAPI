namespace SerbleAPI.Repositories;

/// <summary>Outcome of redeeming a refresh token.</summary>
public class RefreshConsumeResult {
    public bool Success { get; init; }
    /// <summary>True when an already-rotated/revoked token was presented (reuse) and the grant was revoked.</summary>
    public bool Reuse { get; init; }
    public string GrantId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Scopes { get; init; } = "";
    /// <summary>The original user-authentication time, carried forward to refreshed id tokens.</summary>
    public long AuthTimeUnix { get; init; }
}

public interface IOidcRefreshRepository {
    Task Store(string tokenHash, string grantId, string clientId, string userId, string scopes,
        long authTimeUnix, long expiresAtUnix);

    /// <summary>
    /// Atomically rotates a refresh token. On success the presented token is retired and the
    /// caller issues a replacement. If the token was already rotated/revoked, the whole grant
    /// chain is revoked and <see cref="RefreshConsumeResult.Reuse"/> is set.
    /// </summary>
    Task<RefreshConsumeResult> Consume(string tokenHash);

    /// <summary>
    /// Stores a rotated replacement token, guarding against a concurrent reuse-revocation of the
    /// grant chain. Returns false (and self-revokes the new token) if the grant was revoked by a
    /// racing reuse-detection, so the caller can reject the request.
    /// </summary>
    Task<bool> StoreRotation(string tokenHash, string grantId, string clientId, string userId,
        string scopes, long authTimeUnix, long expiresAtUnix);

    Task RevokeGrant(string grantId);
}
