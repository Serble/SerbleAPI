using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IAppRepository {
    OAuthApp? GetOAuthApp(string appId);
    OAuthApp[] GetOAuthAppsFromUser(string userId);
    void AddOAuthApp(OAuthApp app);
    void UpdateOAuthApp(OAuthApp app);
    void DeleteOAuthApp(string appId);
}
