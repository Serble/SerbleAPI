namespace SerbleAPI.Services;

public interface ITokenService {
    /// <summary>
    /// Mints a user login token. <paramref name="issuedAt"/> overrides the stamped issue time, for a
    /// request that has just set a revocation cut-off the new token must survive.
    /// </summary>
    string GenerateLoginToken(string userid, DateTime? issuedAt = null);
    /// <summary>
    /// Validates a user login token. <paramref name="issuedAt"/> is the token's issue time, or null
    /// if it carries none, to weigh against the account's revocation cut-off.
    /// </summary>
    bool ValidateLoginToken(string token, out string? userId, out DateTime? issuedAt);
    
    string GenerateAuthorizationToken(string userId, string appId, string scopeString);
    bool ValidateAuthorizationToken(string token, string appId, out string? userId, out string scopeString,
        out string reason);

    string GenerateAccessToken(string userId, string appId, string scope);
    /// <inheritdoc cref="ValidateLoginToken"/>
    bool ValidateAccessToken(string token, out string? userId, out string? appId, out string scope,
        out DateTime? issuedAt);

    string GenerateRefreshToken(string userId, string appId, string scope);
    bool ValidateRefreshToken(string token, string appId, out string? userId, out string scope);

    string GenerateEmailConfirmationToken(string userId, string email);
    bool ValidateEmailConfirmationToken(string token, out string? userId, out string email);

    /// <summary>
    /// Mints proof that the user just completed a sign-in flow. <paramref name="issuedAt"/> and
    /// <paramref name="expiresAt"/> let a replacement keep the original expiry past a revocation cut-off.
    /// </summary>
    string GenerateReauthToken(string userId, DateTime? issuedAt = null, DateTime? expiresAt = null);
    bool ValidateReauthToken(string token, out string? userId, out DateTime? issuedAt, out DateTime expiresAt);

    string GenerateCheckoutSuccessToken(string productId, string secret);
}
