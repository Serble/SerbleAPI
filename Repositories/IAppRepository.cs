using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IAppRepository {
    Task<OAuthApp?> GetOAuthApp(string appId);
    /// <summary>Loads several apps by id in one query (order not guaranteed; missing ids are omitted).</summary>
    Task<OAuthApp[]> GetOAuthApps(IEnumerable<string> appIds);
    Task<OAuthApp[]> GetOAuthAppsFromUser(string userId);
    Task AddOAuthApp(OAuthApp app);
    Task UpdateOAuthApp(OAuthApp app);
    Task DeleteOAuthApp(string appId);
    Task<long> CountApps();
    Task<OAuthApp[]> SearchApps(string query, int limit);
}
