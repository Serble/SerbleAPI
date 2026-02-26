using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class AppRepository(SerbleDbContext db) : IAppRepository {

    private static OAuthApp Map(DbApp r) => new(r.OwnerId!) {
        Id           = r.Id!,
        Name         = r.Name         ?? "",
        Description  = r.Description  ?? "",
        ClientSecret = r.ClientSecret ?? "",
        RedirectUri  = r.RedirectUri  ?? ""
    };

    public async Task<OAuthApp?> GetOAuthApp(string appId) {
        DbApp? row = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId);
        return row == null ? null : Map(row);
    }

    public async Task<OAuthApp[]> GetOAuthAppsFromUser(string userId) {
        DbApp[] vals = await db.Apps
            .Where(a => a.OwnerId == userId)
            .ToArrayAsync();
        return vals.Select(Map).ToArray();
    }

    public Task AddOAuthApp(OAuthApp app) {
        db.Apps.Add(new DbApp {
            Id           = app.Id,
            OwnerId      = app.OwnerId,
            Name         = app.Name,
            Description  = app.Description,
            ClientSecret = app.ClientSecret,
            RedirectUri  = app.RedirectUri
        });
        return db.SaveChangesAsync();
    }

    public async Task UpdateOAuthApp(OAuthApp app) {
        DbApp? row = await db.Apps.FirstOrDefaultAsync(a => a.Id == app.Id);
        if (row == null) return;
        row.OwnerId      = app.OwnerId;
        row.Name         = app.Name;
        row.Description  = app.Description;
        row.ClientSecret = app.ClientSecret;
        row.RedirectUri  = app.RedirectUri;
        await db.SaveChangesAsync();
    }

    public async Task DeleteOAuthApp(string appId) {
        DbApp? row = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId);
        if (row == null) return;
        db.Apps.Remove(row);
        await db.SaveChangesAsync();
    }
}
