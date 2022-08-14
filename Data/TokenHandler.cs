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
            { "userid", userid }
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
            if (!claims!.ContainsKey("userid")) {
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
    
    private static string GenerateToken(Dictionary<string, string> claims) {
        string mySecret = Program.Config!["token_secret"];
        SymmetricSecurityKey securityKey = new(Encoding.ASCII.GetBytes(mySecret));
        JwtSecurityTokenHandler tokenHandler = new();
        SecurityTokenDescriptor tokenDescriptor = new() {
            Subject = new ClaimsIdentity(claims.Select(c => new Claim(c.Key, c.Value)).ToArray()),
            Expires = DateTime.Now.AddYears(1),
            Issuer = Program.Config!["token_issuer"],
            Audience = Program.Config!["token_audience"],
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256Signature),
        };
        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
        
    public static bool ValidateCurrentToken(string? token, out Dictionary<string, string>? claims, out string failMsg) {
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
        catch (Exception) {
            failMsg = "Validator failed";
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