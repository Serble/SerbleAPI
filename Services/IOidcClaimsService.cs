using System.Security.Claims;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

/// <summary>
/// Builds the scope-derived OIDC claims (profile / email / groups) for both the id token and
/// the userinfo response, so the two never drift apart. The <c>groups</c> claim is driven by
/// the app's admin-configured mapping, not by a user-requested scope.
/// </summary>
public interface IOidcClaimsService {
    Task<List<Claim>> BuildIdTokenClaims(User user, string appId, string[] scopes);
    Task<Dictionary<string, object>> BuildUserInfo(User user, string appId, string[] scopes);
}
