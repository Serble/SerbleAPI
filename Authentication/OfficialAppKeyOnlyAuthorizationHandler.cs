using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement satisfied only when the request authenticated with an app API key
/// and the backing app is flagged official (first-party).
///
/// Use for first-party-only endpoints that must never be reachable via a user or a delegated
/// OAuth token: <c>[Authorize(Policy = "OfficialAppKeyOnly")]</c>. User tokens, OAuth delegated
/// app tokens, and non-official app keys are authenticated but fail this requirement (403).
/// </summary>
public class OfficialAppKeyOnlyRequirement : IAuthorizationRequirement;

public class OfficialAppKeyOnlyAuthorizationHandler(IAppRepository appRepo)
    : AuthorizationHandler<OfficialAppKeyOnlyRequirement> {

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OfficialAppKeyOnlyRequirement requirement) {

        // Must be a pure app API key (not a user token or a delegated OAuth app token).
        if (!context.User.IsAppKey()) return;

        string? appId = context.User.GetAppId();
        if (string.IsNullOrEmpty(appId)) return;

        OAuthApp? app = await appRepo.GetOAuthApp(appId);
        if (app == null) return;
        if (!app.IsOfficial) return;

        context.Succeed(requirement);
    }
}
