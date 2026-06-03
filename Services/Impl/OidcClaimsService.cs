using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Impl;

public class OidcClaimsService(IAppAccessPolicyService accessPolicy) : IOidcClaimsService {

    public async Task<List<Claim>> BuildIdTokenClaims(User user, string appId, string[] scopes) {
        List<Claim> claims = [];
        if (OidcScopes.Contains(scopes, OidcScopes.Profile)) {
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, user.Username));
            claims.Add(new Claim("preferred_username", user.Username));
        }
        if (OidcScopes.Contains(scopes, OidcScopes.Email)) {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
            claims.Add(new Claim("email_verified", user.VerifiedEmail ? "true" : "false", ClaimValueTypes.Boolean));
        }
        foreach (string group in await accessPolicy.GetGroupsClaim(user.Id, appId))
            claims.Add(new Claim("groups", group));
        return claims;
    }

    public async Task<Dictionary<string, object>> BuildUserInfo(User user, string appId, string[] scopes) {
        Dictionary<string, object> info = new() { ["sub"] = user.Id };
        if (OidcScopes.Contains(scopes, OidcScopes.Profile)) {
            info["name"] = user.Username;
            info["preferred_username"] = user.Username;
        }
        if (OidcScopes.Contains(scopes, OidcScopes.Email)) {
            info["email"] = user.Email;
            info["email_verified"] = user.VerifiedEmail;
        }
        string[] groups = await accessPolicy.GetGroupsClaim(user.Id, appId);
        if (groups.Length > 0) info["groups"] = groups;
        return info;
    }
}
