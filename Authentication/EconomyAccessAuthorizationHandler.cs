using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement for economy/balance access. Satisfied when the request is either:
///   - an app authenticating as itself with an API key (no scope concept applies), or
///   - any principal that holds the <c>economy</c> scope (user tokens carry full_access; OAuth
///     delegated app tokens must have been granted the economy scope).
/// </summary>
public class EconomyAccessRequirement : IAuthorizationRequirement;

public class EconomyAccessAuthorizationHandler
    : AuthorizationHandler<EconomyAccessRequirement> {

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EconomyAccessRequirement requirement) {

        if (context.User.IsAppKey() || context.User.HasScope(ScopeHandler.ScopesEnum.Economy)) {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
