using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class UserRepository(SerbleDbContext db) : IUserRepository {

    // ── Mapping helpers ───────────────────────────────────────────────────────

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
        PasswordSalt    = r.PasswordSalt
    };

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<User?> GetUser(string userId) {
        DbUser? row = await db.Users.FindAsync(userId);
        return row == null ? null : Map(row);
    }

    public async Task<User?> GetUserFromName(string userName) {
        DbUser? row = await db.Users.FirstOrDefaultAsync(u => u.Username == userName);
        return row == null ? null : Map(row);
    }

    public async Task<User?> GetUserFromStripeCustomerId(string customerId) {
        DbUser? row = await db.Users.FirstOrDefaultAsync(u => u.SubscriptionId == customerId);
        return row == null ? null : Map(row);
    }

    public async Task<User> AddUser(User user) {
        user.Id = Guid.NewGuid().ToString();
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
            PasswordSalt   = user.PasswordSalt
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
        // Cascade: remove authorized apps too
        await db.UserAuthorizedApps.Where(a => a.UserId == userId).ExecuteDeleteAsync();
        db.Users.Remove(row);
        await db.SaveChangesAsync();
    }

    public Task<long> CountUsers() => db.Users.LongCountAsync();

    // ── Authorized apps ───────────────────────────────────────────────────────

    public async Task AddAuthorizedApp(string userId, AuthorizedApp app) {
        // Remove existing entry for same app so we can replace it cleanly
        await db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == app.AppId)
            .ExecuteDeleteAsync();
        db.UserAuthorizedApps.Add(new DbUserAuthorizedApp {
            UserId = userId,
            AppId  = app.AppId,
            Scopes = app.Scopes
        });
        await db.SaveChangesAsync();
    }

    public Task<AuthorizedApp[]> GetAuthorizedApps(string userId) =>
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId)
            .Select(a => new AuthorizedApp(a.AppId!, a.Scopes!))
            .ToArrayAsync();

    public Task DeleteAuthorizedApp(string userId, string appId) {
        return db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == appId)
            .ExecuteDeleteAsync();
    }
}
