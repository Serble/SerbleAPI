using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class ProductRepository(SerbleDbContext db) : IProductRepository {

    public Task<string[]> GetOwnedProducts(string userId) =>
        db.OwnedProducts
            .Where(p => p.User == userId)
            .Select(p => p.Product!)
            .ToArrayAsync();

    public Task AddOwnedProducts(string userId, string[] productIds) {
        foreach (string productId in productIds) {
            db.OwnedProducts.Add(new DbOwnedProduct { User = userId, Product = productId });
        }
        return db.SaveChangesAsync();
    }

    public async Task RemoveOwnedProduct(string userId, string productId) {
        DbOwnedProduct? row = await db.OwnedProducts
            .FirstOrDefaultAsync(p => p.User == userId && p.Product == productId);
        if (row == null) return;
        db.OwnedProducts.Remove(row);
        await db.SaveChangesAsync();
    }
}
