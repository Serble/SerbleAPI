using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class ProductRepository(SerbleDbContext db) : IProductRepository {

    public string[] GetOwnedProducts(string userId) =>
        db.OwnedProducts
            .Where(p => p.User == userId)
            .Select(p => p.Product!)
            .ToArray();

    public void AddOwnedProducts(string userId, string[] productIds) {
        foreach (string productId in productIds) {
            db.OwnedProducts.Add(new DbOwnedProduct { User = userId, Product = productId });
        }
        db.SaveChanges();
    }

    public void RemoveOwnedProduct(string userId, string productId) {
        DbOwnedProduct? row = db.OwnedProducts
            .FirstOrDefault(p => p.User == userId && p.Product == productId);
        if (row == null) return;
        db.OwnedProducts.Remove(row);
        db.SaveChanges();
    }
}
