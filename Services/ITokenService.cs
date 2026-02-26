namespace SerbleAPI.Services;

public interface ITokenService {
    string GenerateLoginToken(string userid);
    bool ValidateLoginToken(string token, out string? userId);
    
    string GenerateAuthorizationToken(string userId, string appId, string scopeString);
    bool ValidateAuthorizationToken(string token, string appId, out string? userId, out string scopeString,
        out string reason);

    string GenerateAccessToken(string userId, string scope);
    bool ValidateAccessToken(string token, out string? userId, out string scope);

    string GenerateRefreshToken(string userId, string appId, string scope);
    bool ValidateRefreshToken(string token, string appId, out string? userId, out string scope);

    string GenerateEmailConfirmationToken(string userId, string email);
    bool ValidateEmailConfirmationToken(string token, out string? userId, out string email);

    string GenerateFirstStepLoginToken(string userId);
    bool ValidateFirstStepLoginToken(string token, out string? userId);

    string GenerateCheckoutSuccessToken(string productId, string secret);
}
