using Microsoft.AspNetCore.Authorization;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Authentication;

/// <summary>
/// Authorization requirement satisfied only when the request is authenticated
/// with a User token (not an OAuth App token) and the backing user has
/// admin perm level (2).
/// </summary>
public class AdminOnlyRequirement : IAuthorizationRequirement;

public class AdminAuthorizationHandler(IUserRepository userRepo)
    : AuthorizationHandler<AdminOnlyRequirement> {

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminOnlyRequirement requirement) {

        if (!context.User.IsUser()) return;

        string? userId = context.User.GetUserId();
        if (string.IsNullOrEmpty(userId)) return;

        User? user = await userRepo.GetUser(userId);
        if (user == null) return;
        if (!user.IsAdmin()) return;

        context.Succeed(requirement);
    }
}
