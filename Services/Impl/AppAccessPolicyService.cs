using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class AppAccessPolicyService(
    IGroupRepository groupRepo,
    IAppAccessRepository accessRepo) : IAppAccessPolicyService {

    public async Task<AccessDecision> Evaluate(User user, OAuthApp app) {
        AppAccessConfig config = await accessRepo.GetAppAccessConfig(app.Id);

        // Ordered gate. Every failure returns a generic-to-user denial (reason is log-only).
        if (config.AccessPolicy == AppAccessPolicy.Disabled)
            return AccessDecision.Deny("App is disabled for SSO");

        if (user.PermLevel == 0)
            return AccessDecision.Deny("User account is disabled");

        string[] userGroupIds = await groupRepo.GetUserGroupIds(user.Id);

        if (config.DeniedGroupIds.Any(userGroupIds.Contains))
            return AccessDecision.Deny("User is in a denied group");

        switch (config.AccessPolicy) {
            case AppAccessPolicy.RequireVerifiedEmail when !user.VerifiedEmail:
                return AccessDecision.Deny("Email not verified");

            case AppAccessPolicy.RequireMinimumPermLevel
                when user.PermLevel < (config.RequiredPermLevel ?? int.MaxValue):
                return AccessDecision.Deny("PermLevel below required minimum");

            case AppAccessPolicy.RequireGroups
                when !config.AllowedGroupIds.Any(userGroupIds.Contains):
                return AccessDecision.Deny("User not in any allowed group");
        }

        return AccessDecision.Allow();
    }

    public async Task<string[]> GetGroupsClaim(string userId, string appId) {
        AppAccessConfig config = await accessRepo.GetAppAccessConfig(appId);
        if (config.GroupClaimMappings.Count == 0) return [];
        string[] userGroupIds = await groupRepo.GetUserGroupIds(userId);
        return userGroupIds
            .Where(config.GroupClaimMappings.ContainsKey)
            .Select(g => config.GroupClaimMappings[g])
            .Distinct()
            .ToArray();
    }
}
