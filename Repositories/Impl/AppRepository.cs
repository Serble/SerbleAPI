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

    public OAuthApp? GetOAuthApp(string appId) {
        DbApp? row = db.Apps.FirstOrDefault(a => a.Id == appId);
        return row == null ? null : Map(row);
    }

    public OAuthApp[] GetOAuthAppsFromUser(string userId) =>
        db.Apps
            .Where(a => a.OwnerId == userId)
            .AsEnumerable()
            .Select(Map)
            .ToArray();

    public void AddOAuthApp(OAuthApp app) {
        db.Apps.Add(new DbApp {
            Id           = app.Id,
            OwnerId      = app.OwnerId,
            Name         = app.Name,
            Description  = app.Description,
            ClientSecret = app.ClientSecret,
            RedirectUri  = app.RedirectUri
        });
        db.SaveChanges();
    }

    public void UpdateOAuthApp(OAuthApp app) {
        DbApp? row = db.Apps.FirstOrDefault(a => a.Id == app.Id);
        if (row == null) return;
        row.OwnerId      = app.OwnerId;
        row.Name         = app.Name;
        row.Description  = app.Description;
        row.ClientSecret = app.ClientSecret;
        row.RedirectUri  = app.RedirectUri;
        db.SaveChanges();
    }

    public void DeleteOAuthApp(string appId) {
        DbApp? row = db.Apps.FirstOrDefault(a => a.Id == appId);
        if (row == null) return;
        db.Apps.Remove(row);
        db.SaveChanges();
    }
}
