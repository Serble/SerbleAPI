using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Account;

/// <summary>Tokens handed back to the caller when their own change revoked the tokens they used.</summary>
public static class ReplacementTokens {
    public static void Apply(CredentialOverview overview, HttpContext http, ITokenService tokens, string userId,
        DateTime? revokedAt) {
        if (revokedAt is not { } cutoff) return;

        overview.ReplacementToken = tokens.GenerateLoginToken(userId, cutoff);
        if (RequireReauthAttribute.ExpiresAt(http) is { } expires && expires > DateTime.UtcNow) {
            overview.ReplacementReauthToken = tokens.GenerateReauthToken(userId, cutoff, expires);
        }
    }
}
