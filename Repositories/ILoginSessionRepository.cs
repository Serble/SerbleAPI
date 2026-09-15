using SerbleAPI.Models;

namespace SerbleAPI.Repositories;

public interface ILoginSessionRepository {
    Task Create(DbLoginSession session);

    /// <summary>The session, if it exists, is unconsumed and has not expired.</summary>
    Task<DbLoginSession?> GetLive(string idHash);

    Task<bool> AddCompleted(string idHash, int bit);
    Task<bool> TryConsume(string idHash);
    Task<bool> BindUser(string idHash, string userId);
    Task SetPasskeyChallenge(string idHash, string challengeJson);

    /// <summary>Clears the pending challenge; true for exactly one caller.</summary>
    Task<bool> TakePasskeyChallenge(string idHash);

    Task Delete(string idHash);
    Task DeleteExpired();
}
