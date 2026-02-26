using System.Security.Claims;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Authentication;

/// <summary>
/// Convenience extensions for reading Serble-specific claims from a
/// <see cref="ClaimsPrincipal"/> populated by <see cref="SerbleAuthenticationHandler"/>.
/// </summary>
public static class SerbleClaimsPrincipalExtensions {

    /// <summary>The authenticated user's storage ID, or null if not authenticated.</summary>
    public static string? GetUserId(this ClaimsPrincipal p)
        => p.FindFirstValue("userid");

    /// <summary>The raw scope bitmask string (e.g. "10000001").</summary>
    public static string GetScopeString(this ClaimsPrincipal p)
        => p.FindFirstValue("scope") ?? "0";

    /// <summary>Whether the token grants a particular scope (or full_access).</summary>
    public static bool HasScope(this ClaimsPrincipal p, ScopeHandler.ScopesEnum scope)
        => p.GetScopeString().SerbleHasScope(scope);

    /// <summary>Whether the token was issued directly to a user (not an OAuth app).</summary>
    public static bool IsUser(this ClaimsPrincipal p)
        => p.FindFirstValue("auth_type") == "User";

    /// <summary>Whether the token is an OAuth app access token.</summary>
    public static bool IsApp(this ClaimsPrincipal p)
        => p.FindFirstValue("auth_type") == "App";

    /// <summary>The auth type as the legacy enum value.</summary>
    public static SerbleAuthorizationHeaderType GetAuthType(this ClaimsPrincipal p)
        => p.FindFirstValue("auth_type") switch {
            "User" => SerbleAuthorizationHeaderType.User,
            "App"  => SerbleAuthorizationHeaderType.App,
            _      => SerbleAuthorizationHeaderType.Null
        };

    /// <summary>
    /// Loads and returns the full <see cref="User"/> object for the authenticated
    /// principal, with IUserRepository injected so User instance methods work.
    /// Requires the <paramref name="httpContext"/> to resolve the scoped repo.
    /// </summary>
    public static async Task<User?> GetUser(this ClaimsPrincipal p, IUserRepository userRepo) {
        string? userId = p.GetUserId();
        if (userId == null) return null;
        User? user = await userRepo.GetUser(userId);
        return user?.WithRepos(userRepo);
    }
}
