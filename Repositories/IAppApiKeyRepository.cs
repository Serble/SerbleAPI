namespace SerbleAPI.Repositories;

/// <summary>Metadata about an app API key (never includes the plaintext key or hash).</summary>
public class AppApiKeyInfo {
    public string Id { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public DateTime DateCreated { get; set; }
}

/// <summary>Result of creating an API key — includes the plaintext exactly once.</summary>
public class CreatedAppApiKey {
    public AppApiKeyInfo Info { get; set; } = new();
    public string PlaintextKey { get; set; } = "";
}

public interface IAppApiKeyRepository {
    /// <summary>Creates a new API key for an app. The plaintext key is returned only here.</summary>
    Task<CreatedAppApiKey> CreateKey(string appId, string name);
    
    /// <summary>Lists key metadata for an app (no secrets).</summary>
    Task<AppApiKeyInfo[]> GetKeysForApp(string appId);
    
    /// <summary>Deletes a key belonging to an app. Returns true if a key was deleted.</summary>
    Task<bool> DeleteKey(string appId, string keyId);
    
    /// <summary>Resolves a presented plaintext key to its app/key ids, or null if invalid.</summary>
    Task<(string appId, string keyId)?> ResolveKey(string plaintextKey);
}
