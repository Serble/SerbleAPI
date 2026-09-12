using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Config;
using SerbleAPI.Data;

namespace SerbleAPI.Services.Impl; 

public class TokenService(IOptions<JwtSettings> settings, ILogger<TokenService> logger) : ITokenService {
    
    // User Tokens
    // Claims:
    // - userid
    public string GenerateLoginToken(string userid) {
        Dictionary<string, string> claims = new() {
            { "userid", userid },
            { "type", "user" }
        };
        return GenerateToken(claims);
    }
    
    public bool ValidateLoginToken(string token, out string? userId) {
        userId = null;
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId) || !claims.TryGetValue("type", out string? type)) return false;
            return type == "user";
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    /// <summary>
    /// How long a legacy OAuth authorization code is valid. A code is handed to a client which
    /// immediately exchanges it, so it only has to outlive one redirect; it used to inherit the
    /// ten-year default, which made a code found in a log or a browser history a permanent key to
    /// the account.
    /// </summary>
    public static readonly TimeSpan AuthorizationCodeLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Clock tolerance applied when checking a token's <c>nbf</c> and <c>exp</c>.
    ///
    /// <para>The library default is five minutes, which would have quietly turned the two-minute
    /// authorization code above into a seven-minute one. It cannot simply be set to zero, though:
    /// <c>nbf</c> is checked as well as <c>exp</c>, so with no tolerance a token minted by one
    /// instance is rejected as not-yet-valid by another whose clock sits a fraction behind. That
    /// turns ordinary drift between replicas into intermittent authentication failures across every
    /// token type, not just codes.</para>
    ///
    /// <para>Thirty seconds is the compromise: far inside the code's own lifetime, and comfortably
    /// more than NTP-synchronised hosts drift apart.</para>
    /// </summary>
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(30);

    // Authorization Tokens
    // Claims:
    // - userid
    public string GenerateAuthorizationToken(string userId, string appId, string scopeString) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "appid", appId },
            { "scope", scopeString },
            { "type", "oauth-authorization" },
        };
        return GenerateToken(claims, AuthorizationCodeLifetime);
    }
    
    public bool ValidateAuthorizationToken(string token, string appId, out string? userId, out string scopeString, out string reason) {
        userId = null;
        scopeString = "";
        reason = "Unknown Error";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                reason = "Token validation failed: " + validationFailMsg;
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId) 
                || !claims.TryGetValue("type", out string? type) 
                || !claims.TryGetValue("appid", out string? tokenAppId) 
                || !claims.TryGetValue("scope", out scopeString!)) {
                reason = "Missing claims";
                return false;
            }
            if (type != "oauth-authorization") { reason = "Invalid token type"; return false; }
            if (tokenAppId != appId) { reason = "Invalid app id"; return false; }
            reason = "Success";
            return true;
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Access Tokens
    // Claims:
    // - userid
    // - appid (the OAuth app/client the token was issued to)
    public string GenerateAccessToken(string userId, string appId, string scope) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "appid", appId },
            { "scope", scope},
            { "type", "oauth-access" }
        };
        return GenerateToken(claims, 1);
    }
    
    public bool ValidateAccessToken(string token, out string? userId, out string? appId, out string scope) {
        userId = null;
        appId = null;
        scope = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId) 
                || !claims.TryGetValue("type", out string? type) 
                || !claims.TryGetValue("scope", out scope!)) return false;
            // appid is optional for backwards compatibility with tokens issued before it existed.
            claims.TryGetValue("appid", out appId);
            return type == "oauth-access";
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Refresh Tokens
    // Claims:
    // - userid
    // - appid
    // - scope
    public string GenerateRefreshToken(string userId, string appId, string scope) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "appid", appId },
            { "scope", scope},
            { "type", "oauth-refresh" }
        };
        return GenerateToken(claims);
    }
    
    public bool ValidateRefreshToken(string token, string appId, out string? userId, out string scope) {
        userId = null;
        scope = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId)
                || !claims.TryGetValue("type", out string? type) 
                || !claims.TryGetValue("appid", out string? tokenAppId) 
                || !claims.TryGetValue("scope", out scope!)) return false;
            if (type != "oauth-refresh") return false;
            return tokenAppId == appId;
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Email Confirmation Tokens
    // Claims:
    // - userid
    // - email
    public string GenerateEmailConfirmationToken(string userId, string email) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "email", email },
            { "type", "email-confirmation" }
        };
        return GenerateToken(claims);
    }
    
    public bool ValidateEmailConfirmationToken(string token, out string? userId, out string email) {
        userId = null!;
        email = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId)
                || !claims.TryGetValue("type", out string? type) 
                || !claims.TryGetValue("email", out email!)) return false;
            return type == "email-confirmation";
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    // First Step Login Token (To confirm user logged in and is awaiting MFA verification)
    // Claims:
    // - userid
    public string GenerateFirstStepLoginToken(string userId) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "type", "first-step-login" }
        };
        return GenerateToken(claims);
    }
    
    public bool ValidateFirstStepLoginToken(string token, out string? userId) {
        userId = null!;
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId)
                || !claims.TryGetValue("type", out string? type)) return false;
            return type == "first-step-login";
        }
        catch (Exception e) {
            logger.LogDebug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Checkout Success Token (Given to other sites to confirm a successful checkout)
    // Claims:
    // - productid
    public string GenerateCheckoutSuccessToken(string productId, string secret) {
        Dictionary<string, string> claims = new() {
            { "type", "checkout_success" },
            { "productid", productId }
        };
        return GenerateToken(claims, secret: secret);
    }


    private string GenerateToken(Dictionary<string, string> claims, int expirationInHours = 87600, string? secret = null) =>
        GenerateToken(claims, TimeSpan.FromHours(expirationInHours), secret);

    private string GenerateToken(Dictionary<string, string> claims, TimeSpan lifetime, string? secret = null) {
        string mySecret = secret ?? settings.Value.Secret;
        SymmetricSecurityKey securityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        SecurityTokenDescriptor tokenDescriptor = new() {
            Subject = new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)).ToArray()),
            // UtcNow, not Now: a short lifetime has to mean what it says regardless of the host's
            // time zone.
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = settings.Value.Issuer,
            Audience = settings.Value.Audience,
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature),
        };
        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private bool ValidateCurrentToken(string? token, out Dictionary<string, string>? claims, out string failMsg) {
        claims = null;
        failMsg = "Error";
        string mySecret = settings.Value.Secret;
        SymmetricSecurityKey mySecurityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        try {
            tokenHandler.ValidateToken(token, new TokenValidationParameters {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = settings.Value.Issuer,
                ValidAudience = settings.Value.Audience,
                IssuerSigningKey = mySecurityKey,
                ClockSkew = AllowedClockSkew
            }, out SecurityToken _);
        }
        catch (Exception e) {
            failMsg = "Validator failed: " + e.Message;
            return false;
        }
        JwtSecurityTokenHandler tokenHandler2 = new();
        if (tokenHandler2.ReadToken(token) is not JwtSecurityToken securityToken) {
            failMsg = "Token was not a JWT";
            return false;
        }

        // Put all claims in a dictionary
        if (securityToken.Claims == null) return false;
        claims = securityToken.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
        
        // if (claims.ContainsKey("client_secret")) {
        //     // It's an app token but it's being checked as a user token
        //     failMsg = "Token was an app token (depreciated) but was checked as a user token";
        //     return false;
        // }
        
        // If any of the values from TokenClaims are not present in the claims dictionary, return false
        // foreach (string claim in TokenClaims.Claims) {
        //     if (claims.ContainsKey(claim)) continue;
        //     failMsg = $"The claim '{claim}' was not included in the token";
        //     return false;
        // }
        
        failMsg = "Successfully validated token";
        return true;
    }
}