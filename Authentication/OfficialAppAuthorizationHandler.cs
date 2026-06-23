using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement satisfied only when the request is authenticated with
/// an OAuth App access token whose backing app is flagged official (first-party).
///
/// Use this to scaffold official-app-only endpoints:
/// <c>[Authorize(Policy = "OfficialAppOnly")]</c>. User tokens and non-official app
/// tokens are authenticated but fail the requirement, yielding 403.
/// </summary>
public class OfficialAppOnlyRequirement : IAuthorizationRequirement;

public class OfficialAppAuthorizationHandler(IAppRepository appRepo)
    : AuthorizationHandler<OfficialAppOnlyRequirement> {

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OfficialAppOnlyRequirement requirement) {

        // Must be an OAuth app token (not a direct user token).
        if (!context.User.IsApp()) return;

        string? appId = context.User.GetAppId();
        if (string.IsNullOrEmpty(appId)) return;

        OAuthApp? app = await appRepo.GetOAuthApp(appId);
        if (app == null) return;
        if (!app.IsOfficial) return;

        context.Succeed(requirement);
    }
}
