using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement for no-consent economy *modification* (e.g. transferring coins,
/// admin coin set/add/remove). Satisfied when the request is either:
///   - an app authenticating as itself with an API key (an app has full control over its own
///     balance; API keys carry no scope concept), or
///   - any principal holding the <c>manage_economy</c> scope (user tokens carry full_access and
///     always pass).
///
/// The read-only <c>economy</c> scope is intentionally NOT sufficient here.
/// </summary>
public class EconomyManageRequirement : IAuthorizationRequirement;

public class EconomyManageAuthorizationHandler
    : AuthorizationHandler<EconomyManageRequirement> {

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EconomyManageRequirement requirement) {

        if (context.User.IsAppKey() || context.User.HasScope(ScopeHandler.ScopesEnum.ManageEconomy)) {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
