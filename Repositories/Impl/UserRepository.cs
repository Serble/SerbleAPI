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
        DateCreated     = r.DateCreated,
        LastLogin       = r.LastLogin,
        TokensValidFrom = r.TokensValidFrom,
        LastTotpCounter = r.LastTotpCounter
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
            DateCreated    = user.DateCreated,
            LastLogin      = user.LastLogin
        });
        await SaveUsernameWrite(user.Username, user.Id);
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
        row.LastLogin      = user.LastLogin;
        // TokensValidFrom and LastTotpCounter are absent on purpose: both only move forwards, and
        // writing them from a User loaded earlier would move them back, un-revoking tokens or making
        // a spent TOTP code usable. RevokeTokensIssuedBefore and TryConsumeTotpCounter write them.
        await SaveUsernameWrite(user.Username, user.Id);
    }

    /// <summary>
    /// Saves a pending write that carries a username, translating a rejection by the unique
    /// username index into <see cref="UsernameTakenException"/>. The callers check availability
    /// first, so this only fires when a concurrent request claimed the name in between.
    /// </summary>
    private async Task SaveUsernameWrite(string username, string userId) {
        try {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) {
            // Drop the rejected values: left tracked, they would be retried by any later save on
            // this request's context.
            db.ChangeTracker.Clear();
            if (await IsUsernameTaken(username, userId)) throw new UsernameTakenException(username);
            throw;
        }
    }

    /// <summary>
    /// Whether an account other than <paramref name="userId"/> holds the name. Asking the table is
    /// what distinguishes a lost username race from any other failed save, and unlike matching on
    /// the driver's error code or index name it does not depend on which provider is behind EF.
    /// </summary>
    private async Task<bool> IsUsernameTaken(string username, string userId) {
        try {
            return await db.Users.AsNoTracking()
                .AnyAsync(u => u.Username == username && u.Id != userId);
        }
        catch {
            // The database is not answering, so the save failed for some reason we cannot name.
            // Report it as itself rather than guessing at a conflict.
            return false;
        }
    }

    public async Task SetLastLogin(string userId, DateTime lastLogin) {
        DbUser? row = await db.Users.FindAsync(userId);
        if (row == null) return;
        row.LastLogin = lastLogin;
        await db.SaveChangesAsync();
    }

    public Task RevokeTokensIssuedBefore(string userId, DateTime validFrom) =>
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokensValidFrom, validFrom));

    public async Task<bool> TryConsumeTotpCounter(string userId, long counter) {
        int affected = await db.Users
            .Where(u => u.Id == userId && (u.LastTotpCounter == null || u.LastTotpCounter < counter))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpCounter, counter));
        return affected > 0;
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
            GrantType = (int)app.GrantType,
            DateCreated = app.DateCreated == default ? DateTime.UtcNow : app.DateCreated
        });
        await db.SaveChangesAsync();
    }

    public Task<AuthorizedApp[]> GetAuthorizedApps(string userId) =>
        db.UserAuthorizedApps
            .Where(a => a.UserId == userId)
            .Select(a => new AuthorizedApp(a.AppId!, a.Scopes!) {
                GrantType   = (AuthorizedAppGrantType)a.GrantType,
                DateCreated = a.DateCreated
            })
            .ToArrayAsync();

    public Task DeleteAuthorizedApp(string userId, string appId) {
        return db.UserAuthorizedApps
            .Where(a => a.UserId == userId && a.AppId == appId)
            .ExecuteDeleteAsync();
    }
}
