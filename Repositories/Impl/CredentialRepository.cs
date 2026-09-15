using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class CredentialRepository(SerbleDbContext db) : ICredentialRepository {

    private static UserCredential Map(DbUserCredential r) => new() {
        Id         = r.Id,
        UserId     = r.UserId,
        Type       = (CredentialType)r.Type,
        Name       = r.Name,
        Status     = (CredentialStatus)r.Status,
        Secret     = r.Secret,
        Scheme     = r.Scheme,
        LegacySalt = r.LegacySalt,
        Counter    = r.Counter,
        CreatedAt  = r.CreatedAt,
        LastUsedAt = r.LastUsedAt
    };

    public async Task<UserCredential[]> GetCredentials(string userId) =>
        (await db.UserCredentials.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAt)
            .ToArrayAsync())
        .Select(Map).ToArray();

    public async Task<UserCredential?> GetCredential(string userId, string credentialId) {
        DbUserCredential? row = await db.UserCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == credentialId && c.UserId == userId);
        return row == null ? null : Map(row);
    }

    public async Task<UserCredential?> GetActivePassword(string userId) =>
        (await GetActiveCredentials(userId, CredentialType.Password)).FirstOrDefault();

    public async Task<UserCredential[]> GetActiveCredentials(string userId, CredentialType type) =>
        (await db.UserCredentials.AsNoTracking()
            .Where(c => c.UserId == userId && c.Type == (int)type && c.Status == (int)CredentialStatus.Active)
            .ToArrayAsync())
        .Select(Map).ToArray();

    public async Task<int> GetActiveTypeMask(string userId) {
        int[] types = await db.UserCredentials.AsNoTracking()
            .Where(c => c.UserId == userId && c.Status == (int)CredentialStatus.Active)
            .Select(c => c.Type)
            .Distinct()
            .ToArrayAsync();
        return CredentialTypes.ToMask(types.Select(t => (CredentialType)t));
    }

    public async Task<UserCredential> AddCredential(UserCredential credential) {
        if (string.IsNullOrEmpty(credential.Id)) credential.Id = Guid.NewGuid().ToString();
        if (credential.CreatedAt == default) credential.CreatedAt = DateTime.UtcNow;
        db.UserCredentials.Add(new DbUserCredential {
            Id         = credential.Id,
            UserId     = credential.UserId,
            Type       = (int)credential.Type,
            Name       = credential.Name,
            Status     = (int)credential.Status,
            Secret     = credential.Secret,
            Scheme     = credential.Scheme,
            LegacySalt = credential.LegacySalt,
            Counter    = credential.Counter,
            CreatedAt  = credential.CreatedAt,
            LastUsedAt = credential.LastUsedAt
        });
        await db.SaveChangesAsync();
        return credential;
    }

    public async Task RenameCredential(string credentialId, string name) {
        await db.UserCredentials
            .Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, name));
        await db.UserPasskeys
            .Where(p => p.UserCredentialId == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name));
    }

    public Task DeleteCredential(string credentialId) =>
        db.UserCredentials.Where(c => c.Id == credentialId).ExecuteDeleteAsync();

    public Task DeleteCredentials(string userId, CredentialType type) =>
        db.UserCredentials.Where(c => c.UserId == userId && c.Type == (int)type).ExecuteDeleteAsync();

    public Task TouchCredential(string credentialId) =>
        db.UserCredentials
            .Where(c => c.Id == credentialId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, DateTime.UtcNow));

    public Task DeleteStalePending(string userId, CredentialType type, DateTime createdBefore) =>
        db.UserCredentials
            .Where(c => c.UserId == userId && c.Type == (int)type
                        && c.Status == (int)CredentialStatus.Pending && c.CreatedAt < createdBefore)
            .ExecuteDeleteAsync();

    public async Task<bool> SetPassword(string userId, string encodedHash) {
        int replaced = await db.UserCredentials
            .Where(c => c.UserId == userId && c.Type == (int)CredentialType.Password)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Secret, encodedHash)
                .SetProperty(c => c.Scheme, (int)PasswordScheme.Argon2id)
                .SetProperty(c => c.LegacySalt, (string?)null)
                .SetProperty(c => c.Status, (int)CredentialStatus.Active));

        if (replaced == 0) {
            await AddCredential(new UserCredential {
                UserId = userId,
                Type   = CredentialType.Password,
                Status = CredentialStatus.Active,
                Secret = encodedHash,
                Scheme = (int)PasswordScheme.Argon2id
            });
        }

        await db.Users
            .Where(u => u.Id == userId && (u.Password != null || u.PasswordSalt != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Password, (string?)null)
                .SetProperty(u => u.PasswordSalt, (string?)null));

        return replaced > 0;
    }

    public async Task<bool> UpgradePassword(string credentialId, string userId, string oldSecret, string newHash,
        string? legacyDigest) {
        int affected = await db.UserCredentials
            .Where(c => c.Id == credentialId && c.Secret == oldSecret)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Secret, newHash)
                .SetProperty(c => c.Scheme, (int)PasswordScheme.Argon2id)
                .SetProperty(c => c.LegacySalt, (string?)null));
        if (affected == 0) return false;

        if (legacyDigest != null) {
            await db.Users
                .Where(u => u.Id == userId && u.Password == legacyDigest)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.Password, (string?)null)
                    .SetProperty(u => u.PasswordSalt, (string?)null));
        }
        return true;
    }

    public async Task<bool> TryConsumeTotpCounter(string credentialId, long counter) {
        int affected = await db.UserCredentials
            .Where(c => c.Id == credentialId && (c.Counter == null || c.Counter < counter))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Counter, counter));
        return affected > 0;
    }

    public async Task<bool> ActivatePending(string credentialId, long counter) {
        int affected = await db.UserCredentials
            .Where(c => c.Id == credentialId && c.Status == (int)CredentialStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, (int)CredentialStatus.Active)
                .SetProperty(c => c.Counter, counter));
        return affected > 0;
    }

    public async Task<LoginFlow[]> GetFlows(string userId) =>
        (await db.UserLoginFlows.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.CreatedAt)
            .ToArrayAsync())
        .Select(f => new LoginFlow(f.Id, f.MethodMask)).ToArray();

    public Task<int[]> GetFlowMasks(string userId) =>
        db.UserLoginFlows.AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.MethodMask)
            .ToArrayAsync();

    public async Task ReplaceFlows(string userId, IEnumerable<int> masks) {
        int[] wanted = masks.Distinct().ToArray();
        await db.UserLoginFlows
            .Where(f => f.UserId == userId && !wanted.Contains(f.MethodMask))
            .ExecuteDeleteAsync();

        int[] existing = await GetFlowMasks(userId);
        DateTime now = DateTime.UtcNow;
        foreach (int mask in wanted.Except(existing)) {
            db.UserLoginFlows.Add(new DbUserLoginFlow {
                Id         = Guid.NewGuid().ToString(),
                UserId     = userId,
                MethodMask = mask,
                CreatedAt  = now
            });
        }
        await db.SaveChangesAsync();
    }

    public Task<T> WithUserLock<T>(string userId, Func<Task<T>> work) =>
        InTransaction(async () => {
            if (db.Database.IsRelational()) {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Users WHERE Id = {userId} FOR UPDATE");
            }
            return await work();
        });

    public async Task<T> InTransaction<T>(Func<Task<T>> work) {
        if (db.Database.CurrentTransaction != null || !db.Database.IsRelational()) return await work();

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        T result = await work();
        await transaction.CommitAsync();
        return result;
    }
}
