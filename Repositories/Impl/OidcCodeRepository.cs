using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class OidcCodeRepository(SerbleDbContext db) : IOidcCodeRepository {

    public Task StoreCode(OidcAuthorizationCode code, long expiresAtUnix) {
        db.OidcAuthorizationCodes.Add(new DbOidcAuthorizationCode {
            Code                = code.Code,
            ClientId            = code.ClientId,
            UserId              = code.UserId,
            RedirectUri         = code.RedirectUri,
            Scopes              = code.Scopes,
            Nonce               = code.Nonce,
            CodeChallenge       = code.CodeChallenge,
            CodeChallengeMethod = code.CodeChallengeMethod,
            AuthTimeUnix        = code.AuthTimeUnix,
            ExpiresAtUnix       = expiresAtUnix,
            Consumed            = false
        });
        return db.SaveChangesAsync();
    }

    public async Task<OidcAuthorizationCode?> ConsumeCode(string code) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Single atomic UPDATE so a concurrent redemption of the same code can only win once.
        int affected = await db.OidcAuthorizationCodes
            .Where(c => c.Code == code && !c.Consumed && c.ExpiresAtUnix > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Consumed, true));
        if (affected == 0) return null;

        DbOidcAuthorizationCode? row = await db.OidcAuthorizationCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code);
        if (row == null) return null;

        return new OidcAuthorizationCode {
            Code                = row.Code,
            ClientId            = row.ClientId,
            UserId              = row.UserId,
            RedirectUri         = row.RedirectUri,
            Scopes              = row.Scopes,
            Nonce               = row.Nonce,
            CodeChallenge       = row.CodeChallenge,
            CodeChallengeMethod = row.CodeChallengeMethod,
            AuthTimeUnix        = row.AuthTimeUnix
        };
    }
}
