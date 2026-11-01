using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IOidcCodeRepository {
    Task StoreCode(OidcAuthorizationCode code, long expiresAtUnix);

    /// <summary>
    /// Atomically consumes a code (single-use). Returns the bound authorization data, or null
    /// if the code is unknown, already consumed, or expired.
    /// </summary>
    Task<OidcAuthorizationCode?> ConsumeCode(string code);

    /// <summary>
    /// Atomically consumes a cross-app code issued to <paramref name="clientId"/>, for the legacy
    /// token endpoint. Only matches cross-app codes, so a browser-flow OIDC code presented there is
    /// left untouched rather than burnt.
    /// </summary>
    Task<OidcAuthorizationCode?> ConsumeCrossAppCode(string code, string clientId);

    /// <summary>Deletes codes that expired before <paramref name="beforeUnix"/>, consumed or not.</summary>
    Task<int> PurgeExpired(long beforeUnix);
}
