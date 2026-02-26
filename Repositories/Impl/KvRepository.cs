using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class KvRepository(SerbleDbContext db) : IKvRepository {

    public async Task Set(string key, string value) {
        DbKv? row = await db.Kvs.FirstOrDefaultAsync(k => k.Key == key);
        if (row == null) {
            db.Kvs.Add(new DbKv { Key = key, Value = value });
        }
        else {
            row.Value = value;
        }
        await db.SaveChangesAsync();
    }

    public Task<string?> Get(string key) =>
        db.Kvs
            .Where(k => k.Key == key)
            .Select(k => k.Value)
            .FirstOrDefaultAsync();
}
