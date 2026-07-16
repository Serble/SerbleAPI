using System.Security.Claims;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class FeatureFlagService(
    IServerConfigService config,
    IGroupRepository groupRepo,
    IAppRepository appRepo) : IFeatureFlagService {

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
        string? userId = await ResolveGroupUserId(principal);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        string[] allowedGroupIds = await config.GetStringList(feature.GroupsConfigKey);
        if (allowedGroupIds.Length == 0) return false;

        string[] userGroupIds = await groupRepo.GetUserGroupIds(userId);
        HashSet<string> allowed = allowedGroupIds.ToHashSet(StringComparer.Ordinal);
        return userGroupIds.Any(allowed.Contains);
    }

    /// <summary>
    /// Resolves the user id whose groups govern group-mode access. For user and OAuth-app
    /// principals this is the token's own <c>userid</c>. An app authenticating as itself (app
    /// API key) carries no <c>userid</c>, so we fall back to the owner of the app the key
    /// belongs to.
    /// </summary>
    private async Task<string?> ResolveGroupUserId(ClaimsPrincipal? principal) {
        if (principal == null) return null;

        string? userId = principal.GetUserId();
        if (!string.IsNullOrWhiteSpace(userId)) return userId;

        string? appId = principal.GetAppId();
        if (string.IsNullOrWhiteSpace(appId)) return null;

        OAuthApp? app = await appRepo.GetOAuthApp(appId);
        return app?.OwnerId;
    }
}
