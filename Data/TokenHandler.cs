using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GeneralPurposeLib;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data; 

public static class TokenHandler {
    
    // User Tokens
    // Claims:
    // - userid

    public static string GenerateLoginToken(string userid) {
        Dictionary<string, string> claims = new() {
            { "userid", userid },
            { "type", "user" }
        };
        return GenerateToken(claims);
    }
    
    public static bool ValidateLoginToken(string token, out User? user) {
        user = null;
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                Logger.Debug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.ContainsKey("userid") || !claims.ContainsKey("type")) {
                return false;
            }
            if (claims["type"] != "user") {
                return false;
            }
            Program.StorageService!.GetUser(claims["userid"], out User? gottenUser);
            gottenUser.ThrowIfNull();
            user = gottenUser;
            return true;
        }
        catch (Exception e) {
            Logger.Debug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Authorization Tokens
    // Claims:
    // - userid
    
    public static string GenerateAuthorizationToken(string userId, string appId, string scopeString) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "appid", appId },
            { "scope", scopeString },
            { "type", "oauth-authorization" },
        };
        return GenerateToken(claims);
    }
    
    public static bool ValidateAuthorizationToken(string token, string appId, out User? user, out string scopeString, out string reason) {
        user = null;
        scopeString = "";
        reason = "Unknown Error";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                Logger.Debug(validationFailMsg);
                reason = "Token validation failed: " + validationFailMsg;
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.ContainsKey("userid") || !claims.ContainsKey("type") || !claims.ContainsKey("appid") || !claims.ContainsKey("scope")) {
                reason = "Missing claims";
                return false;
            }
            if (claims["type"] != "oauth-authorization") {
                reason = "Invalid token type";
                return false;
            }
            if (claims["appid"] != appId) {
                reason = "Invalid app id";
                Logger.Debug("App ID mismatch: " + claims["appid"] + " (Token) != " + appId + " (Expected)");
                return false;
            }
            Program.StorageService!.GetUser(claims["userid"], out User? gottenUser);
            gottenUser.ThrowIfNull();
            user = gottenUser;
            scopeString = claims["scope"];
            reason = "Success";
            return true;
        }
        catch (Exception e) {
            Logger.Debug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Access Tokens
    // Claims:
    // - userid
    public static string GenerateAccessToken(string userId, string scope) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "scope", scope},
            { "type", "oauth-access" }
        };
        return GenerateToken(claims, 1);
    }
    
    public static bool ValidateAccessToken(string token, out User? user, out string scope) {
        user = null;
        scope = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                Logger.Debug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.ContainsKey("userid") || !claims.ContainsKey("type") || !claims.ContainsKey("scope")) {
                return false;
            }
            if (claims["type"] != "oauth-access") {
                return false;
            }
            Program.StorageService!.GetUser(claims["userid"], out User? gottenUser);
            gottenUser.ThrowIfNull();
            user = gottenUser;
            scope = claims["scope"];
            return true;
        }
        catch (Exception e) {
            Logger.Debug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Refresh Tokens
    // Claims:
    // - userid
    // - appid
    // - scope
    public static string GenerateRefreshToken(string userId, string appId, string scope) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "appid", appId },
            { "scope", scope},
            { "type", "oauth-refresh" }
        };
        return GenerateToken(claims);
    }
    
    public static bool ValidateRefreshToken(string token, string appId, out User? user, out string scope) {
        user = null;
        scope = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                Logger.Debug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.ContainsKey("userid") || !claims.ContainsKey("type") || !claims.ContainsKey("appid") || !claims.ContainsKey("scope")) {
                return false;
            }
            if (claims["type"] != "oauth-refresh") {
                return false;
            }
            if (claims["appid"] != appId) {
                return false;
            }
            Program.StorageService!.GetUser(claims["userid"], out User? gottenUser);
            gottenUser.ThrowIfNull();
            user = gottenUser;
            scope = claims["scope"];
            return true;
        }
        catch (Exception e) {
            Logger.Debug("Token validation failed: " + e);
            return false;
        }
    }
    
    // Email Confirmation Tokens
    // Claims:
    // - userid
    // - email
    public static string GenerateEmailConfirmationToken(string userId, string email) {
        Dictionary<string, string> claims = new() {
            { "userid", userId },
            { "email", email },
            { "type", "email-confirmation" }
        };
        return GenerateToken(claims);
    }
    
    public static bool ValidateEmailConfirmationToken(string token, out User user, out string email) {
        user = null!;
        email = "";
        try {
            if (!ValidateCurrentToken(token, out Dictionary<string, string>? claims, out string validationFailMsg)) {
                Logger.Debug(validationFailMsg);
                return false;
            }
            claims.ThrowIfNull();
            if (!claims!.ContainsKey("userid") || !claims.ContainsKey("type") || !claims.ContainsKey("email")) {
                return false;
            }
            if (claims["type"] != "email-confirmation") {
                return false;
            }
            Program.StorageService!.GetUser(claims["userid"], out User? gottenUser);
            gottenUser.ThrowIfNull();
            user = gottenUser!;
            email = claims["email"];
            return true;
        }
        catch (Exception e) {
            Logger.Debug("Token validation failed: " + e);
            return false;
        }
    }


    private static string GenerateToken(Dictionary<string, string> claims, int expirationInHours = 87600) {
        string mySecret = Program.Config!["token_secret"];
        SymmetricSecurityKey securityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        SecurityTokenDescriptor tokenDescriptor = new() {
            Subject = new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)).ToArray()),
            Expires = DateTime.Now.AddHours(expirationInHours),
            Issuer = Program.Config!["token_issuer"],
            Audience = Program.Config!["token_audience"],
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature),
        };
        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static bool ValidateCurrentToken(string? token, out Dictionary<string, string>? claims, out string failMsg) {
        claims = null;
        failMsg = "Error";
        string mySecret = Program.Config!["token_secret"];
        SymmetricSecurityKey mySecurityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        try {
            tokenHandler.ValidateToken(token, new TokenValidationParameters {
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidIssuer = Program.Config!["token_issuer"],
                ValidAudience = Program.Config!["token_audience"],
                IssuerSigningKey = mySecurityKey
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