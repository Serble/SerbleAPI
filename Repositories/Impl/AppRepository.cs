using System.Text.Json;
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
        RedirectUri  = r.RedirectUri  ?? "",
        AdditionalRedirectUris = DeserializeUris(r.OidcRedirectUris),
        IsPublicClient = r.IsPublicClient,
        RequirePkce    = r.RequirePkce,
        IsOfficial     = r.IsOfficial,
        DateCreated    = r.DateCreated
    };

    private static List<string> DeserializeUris(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }

    private static string? SerializeUris(List<string> uris) =>
        uris.Count == 0 ? null : JsonSerializer.Serialize(uris);

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
            RedirectUri  = app.RedirectUri,
            OidcRedirectUris = SerializeUris(app.AdditionalRedirectUris),
            IsPublicClient   = app.IsPublicClient,
            RequirePkce      = app.RequirePkce,
            IsOfficial       = app.IsOfficial,
            DateCreated      = app.DateCreated == default ? DateTime.UtcNow : app.DateCreated
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
        row.OidcRedirectUris = SerializeUris(app.AdditionalRedirectUris);
        row.IsPublicClient   = app.IsPublicClient;
        row.RequirePkce      = app.RequirePkce;
        row.IsOfficial       = app.IsOfficial;
        await db.SaveChangesAsync();
    }

    public async Task DeleteOAuthApp(string appId) {
        DbApp? row = await db.Apps.FirstOrDefaultAsync(a => a.Id == appId);
        if (row == null) return;
        // remove the app's balances (api keys cascade via FK)
        await db.Balances
            .Where(b => b.OwnerType == (int)BalanceOwnerType.App && b.OwnerId == appId)
            .ExecuteDeleteAsync();
        db.Apps.Remove(row);
        await db.SaveChangesAsync();
    }

    public Task<long> CountApps() => db.Apps.LongCountAsync();

    public async Task<OAuthApp[]> SearchApps(string query, int limit) {
        if (limit <= 0) limit = 25;
        if (limit > 200) limit = 200;
        string q = (query ?? "").Trim();
        IQueryable<DbApp> qry = db.Apps.AsNoTracking();
        if (q.Length > 0) {
            qry = qry.Where(a => EF.Functions.Like(a.Name, $"%{q}%")
                              || EF.Functions.Like(a.Id, $"%{q}%"));
        }
        DbApp[] rows = await qry
            .OrderBy(a => a.Name)
            .Take(limit)
            .ToArrayAsync();
        return rows.Select(Map).ToArray();
    }
}
