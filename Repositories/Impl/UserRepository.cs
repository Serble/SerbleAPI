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

    public User? GetUser(string userId) {
        DbUser? row = db.Users.Find(userId);
        return row == null ? null : Map(row);
    }

    public User? GetUserFromName(string userName) {
        DbUser? row = db.Users.FirstOrDefault(u => u.Username == userName);
        return row == null ? null : Map(row);
    }

    public User? GetUserFromStripeCustomerId(string customerId) {
        DbUser? row = db.Users.FirstOrDefault(u => u.SubscriptionId == customerId);
        return row == null ? null : Map(row);
    }

    public void AddUser(User user, out User newUser) {
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
        db.SaveChanges();
        newUser = user;
    }

    public void UpdateUser(User user) {
        DbUser? row = db.Users.Find(user.Id);
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
        db.SaveChanges();
    }

    public void DeleteUser(string userId) {
        DbUser? row = db.Users.Find(userId);
        if (row == null) return;
        // Cascade: remove authorized apps too
        db.UserAuthorizedApps.Where(a => a.UserId == userId).ExecuteDelete();
        db.Users.Remove(row);
        db.SaveChanges();
    }

    public long CountUsers() => db.Users.LongCount();

    // ── Authorized apps ───────────────────────────────────────────────────────

    public void AddAuthorizedApp(string userId, AuthorizedApp app) {
        // Remove existing entry for same app so we can replace it cleanly
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == app.AppId)
            .ExecuteDelete();
        db.UserAuthorizedApps.Add(new DbUserAuthorizedApp {
            UserId = userId,
            AppId  = app.AppId,
            Scopes = app.Scopes
        });
        db.SaveChanges();
    }

    public AuthorizedApp[] GetAuthorizedApps(string userId) =>
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId)
            .Select(a => new AuthorizedApp(a.AppId!, a.Scopes!))
            .ToArray();

    public void DeleteAuthorizedApp(string userId, string appId) {
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == appId)
            .ExecuteDelete();
    }
}
