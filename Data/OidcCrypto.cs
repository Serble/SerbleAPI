using System.Security.Cryptography;
using System.Text;

namespace SerbleAPI.Data;

/// <summary>
/// Cryptographic helpers for the OIDC provider: high-entropy handles for authorization
/// codes / refresh tokens, refresh-token hashing for at-rest storage, and PKCE S256
/// verification. Distinct from <see cref="SerbleUtils.RandomString"/>, which is not
/// cryptographically secure and must not be used for these values.
/// </summary>
public static class OidcCrypto {

    /// <summary>A URL-safe, high-entropy random handle.</summary>
    public static string NewHandle(int bytes = 32) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>SHA-256 of a refresh token, hex-encoded, for at-rest storage.</summary>
    public static string HashToken(string token) {
        StringBuilder sb = new();
        foreach (byte b in SHA256.HashData(Encoding.UTF8.GetBytes(token)))
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Verifies a PKCE code_verifier against a stored S256 code_challenge.</summary>
    public static bool VerifyPkceS256(string codeVerifier, string codeChallenge) {
        string computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return FixedTimeEquals(computed, codeChallenge);
    }

    /// <summary>Constant-time string comparison (e.g. for client secrets).</summary>
    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
