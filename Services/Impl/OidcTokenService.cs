using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Config;

namespace SerbleAPI.Services.Impl;

public class OidcTokenService(
    IOptions<OidcSettings> settings,
    IOidcKeyService keys,
    ILogger<OidcTokenService> logger) : IOidcTokenService {

    private const string AccessTokenType = "oidc-access";

    public string GenerateIdToken(string userId, string clientId, string? nonce, long authTimeUnix,
        IEnumerable<Claim> extraClaims) {
        List<Claim> claims = [
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.AuthTime, authTimeUnix.ToString(), ClaimValueTypes.Integer64)
        ];
        if (!string.IsNullOrEmpty(nonce)) claims.Add(new Claim(JwtRegisteredClaimNames.Nonce, nonce));
        claims.AddRange(extraClaims);
        return Generate(claims, clientId, settings.Value.IdTokenLifetimeSeconds);
    }

    public string GenerateAccessToken(string userId, string clientId, string scope) {
        List<Claim> claims = [
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim("client_id", clientId),
            new Claim("scope", scope),
            new Claim("token_type", AccessTokenType)
        ];
        // Audience = issuer: the access token is consumed by Serble's own userinfo endpoint.
        return Generate(claims, settings.Value.Issuer, settings.Value.AccessTokenLifetimeSeconds);
    }

    public bool ValidateAccessToken(string token, out OidcAccessTokenInfo? info) {
        info = null;
        try {
            JwtSecurityTokenHandler handler = new();
            handler.ValidateToken(token, new TokenValidationParameters {
                ValidateIssuerSigningKey = true,
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidIssuer              = settings.Value.Issuer,
                ValidAudience            = settings.Value.Issuer,
                IssuerSigningKeys        = keys.GetPublicJsonWebKeySet().Keys,
                ValidAlgorithms          = [SecurityAlgorithms.RsaSha256]
            }, out SecurityToken validated);

            JwtSecurityToken jwt = (JwtSecurityToken)validated;
            string? type = jwt.Claims.FirstOrDefault(c => c.Type == "token_type")?.Value;
            if (type != AccessTokenType) return false;

            string? sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            string? clientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
            string scope = jwt.Claims.FirstOrDefault(c => c.Type == "scope")?.Value ?? "";
            if (sub == null || clientId == null) return false;

            info = new OidcAccessTokenInfo { UserId = sub, ClientId = clientId, Scope = scope };
            return true;
        }
        catch (Exception e) {
            logger.LogDebug("OIDC access token validation failed: {Message}", e.Message);
            return false;
        }
    }

    private string Generate(IEnumerable<Claim> claims, string audience, int lifetimeSeconds) {
        JwtSecurityTokenHandler handler = new();
        SecurityTokenDescriptor descriptor = new() {
            Subject            = new ClaimsIdentity(claims),
            Issuer             = settings.Value.Issuer,
            Audience           = audience,
            IssuedAt           = DateTime.UtcNow,
            Expires            = DateTime.UtcNow.AddSeconds(lifetimeSeconds),
            SigningCredentials = keys.GetActiveSigningCredentials()
        };
        SecurityToken token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }
}
