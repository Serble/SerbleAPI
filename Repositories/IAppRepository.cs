using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IAppRepository {
    Task<OAuthApp?> GetOAuthApp(string appId);
    Task<OAuthApp[]> GetOAuthAppsFromUser(string userId);
    Task AddOAuthApp(OAuthApp app);
    Task UpdateOAuthApp(OAuthApp app);
    Task DeleteOAuthApp(string appId);
}
