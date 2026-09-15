using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>Credentials and the sign-in flows built from them.</summary>
public interface ICredentialRepository {
    Task<UserCredential[]> GetCredentials(string userId);
    Task<UserCredential?> GetCredential(string userId, string credentialId);
    Task<UserCredential?> GetActivePassword(string userId);
    Task<UserCredential[]> GetActiveCredentials(string userId, CredentialType type);

    /// <summary>Bits of every type the user has an active credential of.</summary>
    Task<int> GetActiveTypeMask(string userId);

    Task<UserCredential> AddCredential(UserCredential credential);
    Task RenameCredential(string credentialId, string name);
    Task DeleteCredential(string credentialId);
    Task DeleteCredentials(string userId, CredentialType type);
    Task TouchCredential(string credentialId);
    Task DeleteStalePending(string userId, CredentialType type, DateTime createdBefore);

    /// <summary>
    /// Replaces the user's password, or adds one. Returns whether one was replaced. Any legacy copy in
    /// <c>Users.Password</c> is cleared, since it no longer matches.
    /// </summary>
    Task<bool> SetPassword(string userId, string encodedHash);

    /// <summary>Upgrades a verified password's hash, unless the stored secret changed in the meantime.</summary>
    Task<bool> UpgradePassword(string credentialId, string userId, string oldSecret, string newHash, string? legacyDigest);

    /// <summary>Claims TOTP step <paramref name="counter"/> for the credential; false if it or a later step was used.</summary>
    Task<bool> TryConsumeTotpCounter(string credentialId, long counter);

    /// <summary>Makes a pending credential active, recording the TOTP step that proved it.</summary>
    Task<bool> ActivatePending(string credentialId, long counter);

    Task<LoginFlow[]> GetFlows(string userId);
    Task<int[]> GetFlowMasks(string userId);
    Task ReplaceFlows(string userId, IEnumerable<int> masks);

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction holding a row lock on the user, so concurrent
    /// credential changes for one account cannot each pass a check the other invalidates.
    /// </summary>
    Task<T> WithUserLock<T>(string userId, Func<Task<T>> work);

    Task<T> InTransaction<T>(Func<Task<T>> work);
}
