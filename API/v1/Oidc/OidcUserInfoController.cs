using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using SerbleAPI.Repositories;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Oidc;

/// <summary>
/// The OIDC UserInfo endpoint. Accepts only an OIDC (RS256) access token as a Bearer
/// credential — legacy HS256 Serble tokens and id tokens are rejected — and returns the
/// standard claims permitted by the token's granted scopes.
/// </summary>
[ApiController]
[Route("api/v1/oauth/userinfo")]
public class OidcUserInfoController(
    IOidcTokenService oidcTokens,
    IOidcClaimsService claims,
    IUserRepository userRepo) : ControllerManager {

    [HttpGet]
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> UserInfo() {
        if (!Request.Headers.TryGetValue("Authorization", out StringValues authHeader))
            return Unauthorized();
        string[] parts = authHeader.ToString().Split(' ', 2);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        if (!oidcTokens.ValidateAccessToken(parts[1], out OidcAccessTokenInfo? info) || info == null)
            return Unauthorized();

        User? user = await userRepo.GetUser(info.UserId);
        if (user == null) return Unauthorized();

        string[] scopes = OidcScopes.Parse(info.Scope);
        Dictionary<string, object> userInfo = await claims.BuildUserInfo(user, info.ClientId, scopes);
        return Json(userInfo);
    }
}
