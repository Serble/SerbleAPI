using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class CrossAppGrantRepository(SerbleDbContext db) : ICrossAppGrantRepository {

    public Task<bool> HasGrant(string userId, string callerAppId, string targetAppId) =>
        db.UserCrossAppGrants.AnyAsync(g =>
            g.UserId == userId && g.CallerAppId == callerAppId && g.TargetAppId == targetAppId);

    public Task<CrossAppGrant[]> GetGrants(string userId) =>
        db.UserCrossAppGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.DateCreated)
            .Select(g => new CrossAppGrant {
                CallerAppId = g.CallerAppId,
                TargetAppId = g.TargetAppId,
                DateCreated = g.DateCreated
            })
            .ToArrayAsync();

    public async Task AddGrant(string userId, string callerAppId, string targetAppId) {
        if (await HasGrant(userId, callerAppId, targetAppId)) return;
        db.UserCrossAppGrants.Add(new DbUserCrossAppGrant {
            UserId      = userId,
            CallerAppId = callerAppId,
            TargetAppId = targetAppId,
            DateCreated = DateTime.UtcNow
        });
        try {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) {
            // A concurrent approval of the same pair won the unique index; the consent exists.
            if (!await HasGrant(userId, callerAppId, targetAppId)) throw;
        }
    }

    public Task DeleteGrant(string userId, string callerAppId, string targetAppId) =>
        db.UserCrossAppGrants
            .Where(g => g.UserId == userId && g.CallerAppId == callerAppId && g.TargetAppId == targetAppId)
            .ExecuteDeleteAsync();

    public Task<int> DeleteGrantsInvolving(string userId, string appId) =>
        db.UserCrossAppGrants
            .Where(g => g.UserId == userId && (g.CallerAppId == appId || g.TargetAppId == appId))
            .ExecuteDeleteAsync();
}
