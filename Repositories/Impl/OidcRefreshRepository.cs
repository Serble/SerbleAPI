using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class OidcRefreshRepository(SerbleDbContext db) : IOidcRefreshRepository {

    public Task Store(string tokenHash, string grantId, string clientId, string userId, string scopes,
        long authTimeUnix, long expiresAtUnix) {
        db.OidcRefreshGrants.Add(new DbOidcRefreshGrant {
            TokenHash     = tokenHash,
            GrantId       = grantId,
            ClientId      = clientId,
            UserId        = userId,
            Scopes        = scopes,
            AuthTimeUnix  = authTimeUnix,
            ExpiresAtUnix = expiresAtUnix,
            Rotated       = false,
            Revoked       = false
        });
        return db.SaveChangesAsync();
    }

    public async Task<bool> StoreRotation(string tokenHash, string grantId, string clientId, string userId,
        string scopes, long authTimeUnix, long expiresAtUnix) {
        await Store(tokenHash, grantId, clientId, userId, scopes, authTimeUnix, expiresAtUnix);

        // A reuse-detection on another request may have revoked the chain concurrently. If any
        // sibling row is revoked, this rotation lost the race: revoke the new token too and fail.
        bool revoked = await db.OidcRefreshGrants
            .AnyAsync(g => g.GrantId == grantId && g.Revoked && g.TokenHash != tokenHash);
        if (revoked) {
            await RevokeGrant(grantId);
            return false;
        }
        return true;
    }

    public async Task<RefreshConsumeResult> Consume(string tokenHash) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        DbOidcRefreshGrant? row = await db.OidcRefreshGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.TokenHash == tokenHash);
        if (row == null) return new RefreshConsumeResult { Success = false };

        // Reuse of a retired or revoked token: revoke the entire grant chain as a precaution.
        if (row.Rotated || row.Revoked) {
            await RevokeGrant(row.GrantId);
            return new RefreshConsumeResult { Success = false, Reuse = true, GrantId = row.GrantId };
        }

        if (row.ExpiresAtUnix <= now) return new RefreshConsumeResult { Success = false };

        // Atomically retire this token; a concurrent redemption can only succeed once.
        int affected = await db.OidcRefreshGrants
            .Where(g => g.TokenHash == tokenHash && !g.Rotated && !g.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Rotated, true));
        if (affected == 0) {
            await RevokeGrant(row.GrantId);
            return new RefreshConsumeResult { Success = false, Reuse = true, GrantId = row.GrantId };
        }

        return new RefreshConsumeResult {
            Success      = true,
            GrantId      = row.GrantId,
            ClientId     = row.ClientId,
            UserId       = row.UserId,
            Scopes       = row.Scopes,
            AuthTimeUnix = row.AuthTimeUnix
        };
    }

    public Task RevokeGrant(string grantId) =>
        db.OidcRefreshGrants
            .Where(g => g.GrantId == grantId && !g.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Revoked, true));
}
