using System.Globalization;
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
    public string GenerateLoginToken(string userid, DateTime? issuedAt = null) {
        Dictionary<string, string> claims = new() {
            { "userid", userid },
            { "type", "user" }
        };
        return GenerateToken(claims, TimeSpan.FromHours(DefaultExpirationHours), issuedAt: issuedAt);
    }
    
    public bool ValidateLoginToken(string token, out string? userId, out DateTime? issuedAt) {
        userId = null;
        issuedAt = null;
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.TryGetValue("userid", out userId) || !claims.TryGetValue("type", out string? type)) return false;
            issuedAt = ReadIssuedAt(claims);
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
    
    public bool ValidateAccessToken(string token, out string? userId, out string? appId, out string scope,
        out DateTime? issuedAt) {
        userId = null;
        appId = null;
        scope = "";
        issuedAt = null;
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
            issuedAt = ReadIssuedAt(claims);
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
    
    public static readonly TimeSpan ReauthTokenLifetime = TimeSpan.FromMinutes(10);

    // Reauth Token (proof of a recently completed sign-in flow, required for credential changes)
    // Claims:
    // - userid
    public string GenerateReauthToken(string userId, DateTime? issuedAt = null, DateTime? expiresAt = null) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "type", "reauth" }
        };
        return GenerateToken(claims, ReauthTokenLifetime, issuedAt: issuedAt, expiresAt: expiresAt);
    }

    public bool ValidateReauthToken(string token, out string? userId, out DateTime? issuedAt, out DateTime expiresAt) {
        userId = null;
        issuedAt = null;
        expiresAt = default;
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                logger.LogDebug(validationFailMsg);
                return false;
            }
            if (!claims!.TryGetValue("userid", out userId)
                || !claims.TryGetValue("type", out string? type)
                || type != "reauth"
                || !claims.TryGetValue(JwtRegisteredClaimNames.Exp, out string? rawExp)
                || !long.TryParse(rawExp, NumberStyles.Integer, CultureInfo.InvariantCulture, out long exp)) return false;
            issuedAt = ReadIssuedAt(claims);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
            return true;
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


    /// <summary>Ten years. The lifetime every token type gets unless it names its own.</summary>
    private const int DefaultExpirationHours = 87600;

    private string GenerateToken(Dictionary<string, string> claims, int expirationInHours = DefaultExpirationHours,
        string? secret = null) =>
        GenerateToken(claims, TimeSpan.FromHours(expirationInHours), secret);

    /// <summary>
    /// The token's issue time in whole milliseconds since the epoch. The standard <c>iat</c> counts
    /// whole seconds, too coarse to tell a revocation cut-off from a token minted in the same second;
    /// it stays on the token as well, so anything reading an ordinary JWT still finds it.
    /// </summary>
    public const string IssuedAtMsClaim = "iat_ms";

    /// <param name="issuedAt">
    /// What to stamp as the token's issue time, or null for now. Pass a revocation cut-off set by the
    /// same request: a token exactly as old as the cut-off is the one thing it does not reject.
    /// </param>
    /// <param name="expiresAt">Overrides <paramref name="lifetime"/>.</param>
    private string GenerateToken(Dictionary<string, string> claims, TimeSpan lifetime, string? secret = null,
        DateTime? issuedAt = null, DateTime? expiresAt = null) {
        string mySecret = secret ?? settings.Value.Secret;
        SymmetricSecurityKey securityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        // UtcNow, not Now: a short lifetime has to mean what it says regardless of the host's time
        // zone. Milliseconds, to match the precision a revocation cut-off is recorded at.
        DateTime now = TruncateToMilliseconds(issuedAt ?? DateTime.UtcNow);
        List<Claim> tokenClaims = claims.Select(c => new Claim(c.Key, c.Value)).ToList();
        tokenClaims.Add(new Claim(IssuedAtMsClaim,
            new DateTimeOffset(now, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        SecurityTokenDescriptor tokenDescriptor = new() {
            Subject = new ClaimsIdentity(tokenClaims),
            // Set explicitly: revocation falls back to this for tokens predating iat_ms.
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt ?? now.Add(lifetime),
            Issuer = settings.Value.Issuer,
            Audience = settings.Value.Audience,
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature),
        };
        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// When the token was issued, as UTC, or null if it says nothing usable — which callers read as
    /// "too old to accept", so an unstamped token fails a cut-off rather than slipping past it.
    /// Falls back to the second-resolution <c>iat</c>, taken as the start of that second so a token
    /// from the cut-off's own second counts as older than it.
    /// </summary>
    private static DateTime? ReadIssuedAt(Dictionary<string, string> claims) {
        if (claims.TryGetValue(IssuedAtMsClaim, out string? rawMs)
            && long.TryParse(rawMs, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

        if (!claims.TryGetValue(JwtRegisteredClaimNames.Iat, out string? raw)) return null;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)) return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    private static DateTime TruncateToMilliseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);

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