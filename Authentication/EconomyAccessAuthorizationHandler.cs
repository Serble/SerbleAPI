using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement for read access to economy/balance data. Satisfied when the
/// request is either:
///   - an app authenticating as itself with an API key (no scope concept applies), or
///   - any principal holding the <c>economy</c> scope (read) or the <c>manage_economy</c> scope
///     (which implies read). User tokens carry full_access and always pass.
/// </summary>
public class EconomyAccessRequirement : IAuthorizationRequirement;

public class EconomyAccessAuthorizationHandler
    : AuthorizationHandler<EconomyAccessRequirement> {

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EconomyAccessRequirement requirement) {

        if (context.User.IsAppKey()
            || context.User.HasScope(ScopeHandler.ScopesEnum.Economy)
            || context.User.HasScope(ScopeHandler.ScopesEnum.ManageEconomy)) {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
