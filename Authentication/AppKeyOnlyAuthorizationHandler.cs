using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Repositories;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement satisfied only when the request authenticated with an app API key
/// (the app acting as itself) and the backing app still exists.
///
/// Use for app-only endpoints: <c>[Authorize(Policy = "AppOnly")]</c>. User tokens and OAuth
/// delegated app tokens are authenticated but fail this requirement (403).
/// </summary>
public class AppKeyOnlyRequirement : IAuthorizationRequirement;

public class AppKeyOnlyAuthorizationHandler(IAppRepository appRepo)
    : AuthorizationHandler<AppKeyOnlyRequirement> {

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppKeyOnlyRequirement requirement) {

        if (!context.User.IsAppKey()) return;

        string? appId = context.User.GetAppId();
        if (string.IsNullOrEmpty(appId)) return;

        if (await appRepo.GetOAuthApp(appId) == null) return;

        context.Succeed(requirement);
    }
}
