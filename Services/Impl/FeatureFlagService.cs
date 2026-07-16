using System.Security.Claims;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class FeatureFlagService(
    IServerConfigService config,
    IGroupRepository groupRepo) : IFeatureFlagService {

    public async Task<bool?> IsEnabled(string feature, ClaimsPrincipal? principal = null) {
        FeatureFlagDefinition? def = FeatureFlagCatalog.Find(feature);
        return def == null ? null : await IsEnabled(def, principal);
    }

    public async Task<bool> IsEnabled(FeatureFlagDefinition feature, ClaimsPrincipal? principal = null) {
        ServerConfigItem? modeItem = await config.Get(feature.ModeConfigKey);
        string mode = (modeItem?.Value ?? "disabled").Trim().ToLowerInvariant();

        return mode switch {
            "enabled" => true,
            "groups"  => await IsEnabledForGroup(feature, principal),
            _         => false
        };
    }

    private async Task<bool> IsEnabledForGroup(FeatureFlagDefinition feature, ClaimsPrincipal? principal) {
        string? userId = principal?.GetUserId();
        if (string.IsNullOrWhiteSpace(userId)) return false;

        string[] allowedGroupIds = await config.GetStringList(feature.GroupsConfigKey);
        if (allowedGroupIds.Length == 0) return false;

        string[] userGroupIds = await groupRepo.GetUserGroupIds(userId);
        HashSet<string> allowed = allowedGroupIds.ToHashSet(StringComparer.Ordinal);
        return userGroupIds.Any(allowed.Contains);
    }
}
