using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class UserRepository(SerbleDbContext db) : IUserRepository {

    // Mapping helpers

    private static User Map(DbUser r) => new() {
        Id              = r.Id,
        Username        = r.Username        ?? "",
        Email           = r.Email           ?? "",
        VerifiedEmail   = r.VerifiedEmail,
        PasswordHash    = r.Password        ?? "",
        PermLevel       = r.PermLevel,
        StripeCustomerId = r.SubscriptionId,
        Language        = r.Language,
        TotpEnabled     = r.TotpEnabled,
        TotpSecret      = r.TotpSecret,
        PasswordSalt    = r.PasswordSalt,
        DateCreated     = r.DateCreated
    };
    
    private User? MapWithRepos(DbUser? r) {
        if (r == null) return null;
        User user = Map(r);
        user.WithRepos(this);
        return user;
    }

    // Users

    public async Task<User?> GetUser(string userId) {
        DbUser? row = await db.Users.FindAsync(userId);
        return MapWithRepos(row);
    }

    public async Task<User[]> GetUsers(string[] userIds) {
        string[] ids = (userIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        if (ids.Length == 0) return [];
        DbUser[] rows = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToArrayAsync();
        return rows.Select(r => MapWithRepos(r)!).ToArray();
    }

    public async Task<User?> GetUserFromName(string userName) {
        DbUser? row = await db.Users.FirstOrDefaultAsync(u => u.Username == userName);
        return MapWithRepos(row);
    }

    public async Task<User?> GetUserFromStripeCustomerId(string customerId) {
        DbUser? row = await db.Users.FirstOrDefaultAsync(u => u.SubscriptionId == customerId);
        return MapWithRepos(row);
    }

    public async Task<User> AddUser(User user) {
        user.Id = Guid.NewGuid().ToString();
        user.DateCreated = DateTime.UtcNow;
        db.Users.Add(new DbUser {
            Id             = user.Id,
            Username       = user.Username,
            Email          = user.Email,
            VerifiedEmail  = user.VerifiedEmail,
            Password       = user.PasswordHash,
            PermLevel      = user.PermLevel,
            SubscriptionId = user.StripeCustomerId,
            Language       = user.Language,
            TotpEnabled    = user.TotpEnabled,
            TotpSecret     = user.TotpSecret,
            PasswordSalt   = user.PasswordSalt,
            DateCreated    = user.DateCreated
        });
        await db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateUser(User user) {
        DbUser? row = await db.Users.FindAsync(user.Id);
        if (row == null) return;
        row.Username       = user.Username;
        row.Email          = user.Email;
        row.VerifiedEmail  = user.VerifiedEmail;
        row.Password       = user.PasswordHash;
        row.PermLevel      = user.PermLevel;
        row.SubscriptionId = user.StripeCustomerId;
        row.Language       = user.Language;
        row.TotpEnabled    = user.TotpEnabled;
        row.TotpSecret     = user.TotpSecret;
        row.PasswordSalt   = user.PasswordSalt;
        await db.SaveChangesAsync();
    }

    public async Task DeleteUser(string userId) {
        DbUser? row = await db.Users.FindAsync(userId);
        if (row == null) return;
        // remove authorised apps too
        await db.UserAuthorizedApps.Where(a => a.UserId == userId).ExecuteDeleteAsync();
        // remove balances too
        await db.Balances
            .Where(b => b.OwnerType == (int)BalanceOwnerType.User && b.OwnerId == userId)
            .ExecuteDeleteAsync();
        db.Users.Remove(row);
        await db.SaveChangesAsync();
    }

    public Task<long> CountUsers() => db.Users.LongCountAsync();

    public Task<long> CountVerifiedEmailUsers() =>
        db.Users.LongCountAsync(u => u.VerifiedEmail);

    public async Task<User[]> SearchUsers(string query, int limit) {
        if (limit <= 0) limit = 25;
        if (limit > 200) limit = 200;
        string q = (query ?? "").Trim();
        IQueryable<DbUser> qry = db.Users.AsNoTracking();
        if (q.Length > 0) {
            qry = qry.Where(u => EF.Functions.Like(u.Username, $"%{q}%")
                              || (u.Email != null && EF.Functions.Like(u.Email, $"%{q}%")));
        }
        DbUser[] rows = await qry
            .OrderBy(u => u.Username)
            .Take(limit)
            .ToArrayAsync();
        return rows.Select(r => MapWithRepos(r)!).ToArray();
    }

    // Authorised apps

    public async Task AddAuthorizedApp(string userId, AuthorizedApp app) {
        // Remove existing entry for same app so we can replace it cleanly
        await db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == app.AppId)
            .ExecuteDeleteAsync();
        db.UserAuthorizedApps.Add(new DbUserAuthorizedApp {
            UserId = userId,
            AppId  = app.AppId,
            Scopes = app.Scopes,
            DateCreated = app.DateCreated == default ? DateTime.UtcNow : app.DateCreated
        });
        await db.SaveChangesAsync();
    }

    public Task<AuthorizedApp[]> GetAuthorizedApps(string userId) =>
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId)
            .Select(a => new AuthorizedApp(a.AppId!, a.Scopes!) { DateCreated = a.DateCreated })
            .ToArrayAsync();

    public Task DeleteAuthorizedApp(string userId, string appId) {
        return db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == appId)
            .ExecuteDeleteAsync();
    }
}
