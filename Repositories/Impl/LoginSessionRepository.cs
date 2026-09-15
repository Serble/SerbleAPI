using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class LoginSessionRepository(SerbleDbContext db) : ILoginSessionRepository {

    public Task Create(DbLoginSession session) {
        db.LoginSessions.Add(session);
        return db.SaveChangesAsync();
    }

    public Task<DbLoginSession?> GetLive(string idHash) {
        DateTime now = DateTime.UtcNow;
        return db.LoginSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdHash == idHash && !s.Consumed && s.ExpiresAt > now);
    }

    public async Task<bool> AddCompleted(string idHash, int bit) {
        DateTime now = DateTime.UtcNow;
        int affected = await db.LoginSessions
            .Where(s => s.IdHash == idHash && !s.Consumed && s.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedMask, x => x.CompletedMask | bit));
        return affected > 0;
    }

    public async Task<bool> TryConsume(string idHash) {
        int affected = await db.LoginSessions
            .Where(s => s.IdHash == idHash && !s.Consumed)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Consumed, true));
        return affected > 0;
    }

    public async Task<bool> BindUser(string idHash, string userId) {
        int affected = await db.LoginSessions
            .Where(s => s.IdHash == idHash && s.UserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, userId));
        return affected > 0;
    }

    public Task SetPasskeyChallenge(string idHash, string challengeJson) =>
        db.LoginSessions
            .Where(s => s.IdHash == idHash)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasskeyChallenge, challengeJson));

    public async Task<bool> TakePasskeyChallenge(string idHash) {
        int affected = await db.LoginSessions
            .Where(s => s.IdHash == idHash && s.PasskeyChallenge != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasskeyChallenge, (string?)null));
        return affected > 0;
    }

    public Task Delete(string idHash) =>
        db.LoginSessions.Where(s => s.IdHash == idHash).ExecuteDeleteAsync();

    public Task DeleteExpired() {
        DateTime now = DateTime.UtcNow;
        return db.LoginSessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync();
    }
}
