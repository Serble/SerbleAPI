using Microsoft.IdentityModel.Tokens;

namespace SerbleAPI.Services;

/// <summary>
/// Provides the RSA signing material for the OIDC provider. Keys are loaded once from
/// <c>OidcSettings</c>; the active key signs new tokens and every loaded key is exposed as
/// a public JWK so relying parties can verify tokens across a rotation.
/// </summary>
public interface IOidcKeyService {
    /// <summary>The credentials (private key + <c>kid</c>) used to sign new tokens.</summary>
    SigningCredentials GetActiveSigningCredentials();

    /// <summary>The public JWK Set exposed at the jwks_uri.</summary>
    JsonWebKeySet GetPublicJsonWebKeySet();
}
