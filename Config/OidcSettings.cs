namespace SerbleAPI.Config;

/// <summary>
/// Configuration for the OpenID Connect provider. The <see cref="Issuer"/> MUST be the
/// public URL clients reach Serble on (it is published in the discovery document and used
/// as the <c>iss</c> claim). At least one signing key is required; the first enabled key
/// is used to sign new tokens while all keys are published in the JWKS for rotation.
/// </summary>
public class OidcSettings {
    public string Issuer { get; set; } = null!;
    public OidcSigningKeySettings[] SigningKeys { get; set; } = [];

    public int AuthorizationCodeLifetimeSeconds { get; set; } = 300;
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;
    public int IdTokenLifetimeSeconds { get; set; } = 900;
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>Whether public (secret-less) clients must supply a PKCE challenge.</summary>
    public bool RequirePkceForPublicClients { get; set; } = true;
}

/// <summary>
/// A single RSA signing key. Either <see cref="PrivateKeyPem"/> (inline PKCS#8 PEM) or
/// <see cref="PrivateKeyPath"/> (path to a PEM file) must be set. <see cref="Kid"/> is the
/// key id advertised in the JWKS and the token header.
/// </summary>
public class OidcSigningKeySettings {
    public string Kid { get; set; } = null!;
    public string? PrivateKeyPem { get; set; }
    public string? PrivateKeyPath { get; set; }
    public bool Enabled { get; set; } = true;
}
