using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>
/// Reads and writes the admin-only access configuration for apps (login gate, group
/// allow/deny lists and group→claim mappings). Kept separate from <see cref="IAppRepository"/>
/// so this metadata never travels through owner-facing app responses.
/// </summary>
public interface IAppAccessRepository {
    Task<AppAccessConfig> GetAppAccessConfig(string appId);
    Task SetAccessPolicy(string appId, AppAccessPolicy policy, int? requiredPermLevel);
    Task SetGroupRules(string appId, string[] allowedGroupIds, string[] deniedGroupIds);
    Task SetGroupClaimMappings(string appId, Dictionary<string, string> mappings);
}
