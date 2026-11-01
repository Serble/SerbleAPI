using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>Users' consents for one app to sign them in to another (cross-app sign-in).</summary>
public interface ICrossAppGrantRepository {
    Task<bool> HasGrant(string userId, string callerAppId, string targetAppId);
    Task<CrossAppGrant[]> GetGrants(string userId);
    /// <summary>Records consent for the pair; a no-op if it already exists.</summary>
    Task AddGrant(string userId, string callerAppId, string targetAppId);
    Task DeleteGrant(string userId, string callerAppId, string targetAppId);
    /// <summary>Drops every consent of the user's that names <paramref name="appId"/> on either side.</summary>
    Task<int> DeleteGrantsInvolving(string userId, string appId);
}
