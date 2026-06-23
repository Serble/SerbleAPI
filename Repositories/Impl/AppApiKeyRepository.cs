using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class AppApiKeyRepository(SerbleDbContext db) : IAppApiKeyRepository {

    public const string KeyPrefixLiteral = "sap_";
    private const int RandomLength = 48;

    private static AppApiKeyInfo MapInfo(DbAppApiKey r) => new() {
        Id          = r.Id,
        AppId       = r.AppId,
        Name        = r.Name,
        KeyPrefix   = r.KeyPrefix,
        DateCreated = r.DateCreated
    };

    public async Task<CreatedAppApiKey> CreateKey(string appId, string name) {
        string plaintext = KeyPrefixLiteral + SerbleUtils.RandomString(RandomLength);
        // Non-secret preview: the literal prefix plus the first few random chars.
        string preview = plaintext[..Math.Min(plaintext.Length, KeyPrefixLiteral.Length + 6)] + "…";

        DbAppApiKey row = new() {
            Id          = Guid.NewGuid().ToString(),
            AppId       = appId,
            Name        = name,
            KeyHash     = plaintext.Sha256Hash(),
            KeyPrefix   = preview,
            DateCreated = DateTime.UtcNow
        };
        db.AppApiKeys.Add(row);
        await db.SaveChangesAsync();

        return new CreatedAppApiKey {
            Info = MapInfo(row),
            PlaintextKey = plaintext
        };
    }

    public async Task<AppApiKeyInfo[]> GetKeysForApp(string appId) {
        DbAppApiKey[] rows = await db.AppApiKeys.AsNoTracking()
            .Where(k => k.AppId == appId)
            .OrderBy(k => k.DateCreated)
            .ToArrayAsync();
        return rows.Select(MapInfo).ToArray();
    }

    public async Task<bool> DeleteKey(string appId, string keyId) {
        int deleted = await db.AppApiKeys
            .Where(k => k.AppId == appId && k.Id == keyId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    public async Task<(string appId, string keyId)?> ResolveKey(string plaintextKey) {
        if (string.IsNullOrEmpty(plaintextKey) || !plaintextKey.StartsWith(KeyPrefixLiteral))
            return null;
        string hash = plaintextKey.Sha256Hash();
        DbAppApiKey? row = await db.AppApiKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash);
        return row == null ? null : (row.AppId, row.Id);
    }
}
