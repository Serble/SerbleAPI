using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class KvRepository(SerbleDbContext db) : IKvRepository {

    public void Set(string key, string value) {
        DbKv? row = db.Kvs.FirstOrDefault(k => k.Key == key);
        if (row == null) {
            db.Kvs.Add(new DbKv { Key = key, Value = value });
        }
        else {
            row.Value = value;
        }
        db.SaveChanges();
    }

    public string? Get(string key) =>
        db.Kvs
            .Where(k => k.Key == key)
            .Select(k => k.Value)
            .FirstOrDefault();
}
