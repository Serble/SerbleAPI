using System.Security.Claims;

namespace SerbleAPI.Services;

/// <summary>
/// Issues and validates the RS256 JWTs used by the OIDC provider (id tokens and OIDC
/// access tokens). This is intentionally separate from <see cref="ITokenService"/>, which
/// continues to mint the legacy HS256 Serble tokens; the two signing worlds never mix.
/// </summary>
public interface IOidcTokenService {

    /// <summary>
    /// Builds an id token for the given user/client. <paramref name="extraClaims"/> carries
    /// the scope-derived profile/email/groups claims assembled by the caller.
    /// </summary>
    string GenerateIdToken(string userId, string clientId, string? nonce, long authTimeUnix,
        IEnumerable<Claim> extraClaims);

    /// <summary>Builds an OIDC access token (audience = issuer, consumed by the userinfo endpoint).</summary>
    string GenerateAccessToken(string userId, string clientId, string scope);

    /// <summary>Validates an OIDC access token. Rejects legacy HS256 tokens and id tokens.</summary>
    bool ValidateAccessToken(string token, out OidcAccessTokenInfo? info);
}

/// <summary>Claims extracted from a validated OIDC access token.</summary>
public class OidcAccessTokenInfo {
    public string UserId { get; init; } = null!;
    public string ClientId { get; init; } = null!;
    public string Scope { get; init; } = null!;
}
