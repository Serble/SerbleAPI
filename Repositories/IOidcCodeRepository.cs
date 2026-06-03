using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IOidcCodeRepository {
    Task StoreCode(OidcAuthorizationCode code, long expiresAtUnix);

    /// <summary>
    /// Atomically consumes a code (single-use). Returns the bound authorization data, or null
    /// if the code is unknown, already consumed, or expired.
    /// </summary>
    Task<OidcAuthorizationCode?> ConsumeCode(string code);
}
