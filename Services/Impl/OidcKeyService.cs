using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Config;

namespace SerbleAPI.Services.Impl;

public class OidcKeyService : IOidcKeyService {

    private readonly SigningCredentials _activeCredentials;
    private readonly JsonWebKeySet _publicKeySet;

    public OidcKeyService(IOptions<OidcSettings> settings, ILogger<OidcKeyService> logger) {
        OidcSigningKeySettings[] keys = settings.Value.SigningKeys
            .Where(k => k.Enabled)
            .ToArray();
        if (keys.Length == 0)
            throw new InvalidOperationException("OIDC requires at least one enabled signing key in Oidc:SigningKeys");

        SigningCredentials? active = null;
        JsonWebKeySet keySet = new();
        foreach (OidcSigningKeySettings key in keys) {
            RSA rsa = RSA.Create();
            rsa.ImportFromPem(LoadPem(key));

            RsaSecurityKey securityKey = new(rsa) { KeyId = key.Kid };
            active ??= new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            // Export the public half only for the JWKS.
            RSA pub = RSA.Create();
            pub.ImportParameters(rsa.ExportParameters(false));
            JsonWebKey jwk = JsonWebKeyConverter.ConvertFromSecurityKey(new RsaSecurityKey(pub) { KeyId = key.Kid });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            keySet.Keys.Add(jwk);
        }

        _activeCredentials = active!;
        _publicKeySet = keySet;
        logger.LogInformation("Loaded {Count} OIDC signing key(s); signing with kid {Kid}",
            keys.Length, _activeCredentials.Key.KeyId);
    }

    public SigningCredentials GetActiveSigningCredentials() => _activeCredentials;

    public JsonWebKeySet GetPublicJsonWebKeySet() => _publicKeySet;

    private static string LoadPem(OidcSigningKeySettings key) {
        if (!string.IsNullOrWhiteSpace(key.PrivateKeyPem)) return key.PrivateKeyPem;
        if (!string.IsNullOrWhiteSpace(key.PrivateKeyPath)) return File.ReadAllText(key.PrivateKeyPath);
        throw new InvalidOperationException($"OIDC signing key '{key.Kid}' has neither PrivateKeyPem nor PrivateKeyPath set");
    }
}
