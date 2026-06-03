using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class AppAccessRepository(SerbleDbContext db) : IAppAccessRepository {

    public async Task<AppAccessConfig> GetAppAccessConfig(string appId) {
        DbApp? app = await db.Apps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appId);
        DbAppGroupRule[] rules = await db.AppGroupRules.AsNoTracking()
            .Where(r => r.AppId == appId).ToArrayAsync();
        DbAppGroupClaim[] claims = await db.AppGroupClaims.AsNoTracking()
            .Where(c => c.AppId == appId).ToArrayAsync();

        return new AppAccessConfig {
            AppId             = appId,
            AccessPolicy      = (AppAccessPolicy)(app?.AccessPolicy ?? 0),
            RequiredPermLevel = app?.RequiredPermLevel,
            AllowedGroupIds   = rules.Where(r => !r.Deny).Select(r => r.GroupId).ToArray(),
            DeniedGroupIds    = rules.Where(r => r.Deny).Select(r => r.GroupId).ToArray(),
            GroupClaimMappings = claims.ToDictionary(c => c.GroupId, c => c.ClaimValue)
        };
    }

    public async Task SetAccessPolicy(string appId, AppAccessPolicy policy, int? requiredPermLevel) {
        DbApp? app = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId);
        if (app == null) return;
        app.AccessPolicy      = (int)policy;
        app.RequiredPermLevel = requiredPermLevel;
        await db.SaveChangesAsync();
    }

    public async Task SetGroupRules(string appId, string[] allowedGroupIds, string[] deniedGroupIds) {
        await db.AppGroupRules.Where(r => r.AppId == appId).ExecuteDeleteAsync();
        foreach (string groupId in allowedGroupIds.Distinct())
            db.AppGroupRules.Add(new DbAppGroupRule { AppId = appId, GroupId = groupId, Deny = false });
        foreach (string groupId in deniedGroupIds.Distinct())
            db.AppGroupRules.Add(new DbAppGroupRule { AppId = appId, GroupId = groupId, Deny = true });
        await db.SaveChangesAsync();
    }

    public async Task SetGroupClaimMappings(string appId, Dictionary<string, string> mappings) {
        await db.AppGroupClaims.Where(c => c.AppId == appId).ExecuteDeleteAsync();
        foreach ((string groupId, string claimValue) in mappings)
            db.AppGroupClaims.Add(new DbAppGroupClaim { AppId = appId, GroupId = groupId, ClaimValue = claimValue });
        await db.SaveChangesAsync();
    }
}
