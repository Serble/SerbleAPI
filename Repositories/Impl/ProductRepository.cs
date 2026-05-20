using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SerbleAPI.Data.Schemas;
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

    public async Task<SerbleProduct?> GetProductFromId(string id) {
        DbSerbleProduct? row = await db.SerbleProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return row == null ? null : SerbleProduct.FromDb(row);
    }

    public async Task<SerbleProduct?> GetProductFromPriceId(string priceId) {
        // PriceIds is JSON-encoded so we do the filter client-side (product list is small).
        DbSerbleProduct[] rows = await db.SerbleProducts.AsNoTracking().ToArrayAsync();
        return rows
            .Select(SerbleProduct.FromDb)
            .FirstOrDefault(p => p.PriceIds.Contains(priceId));
    }

    public async Task<SerbleProduct?[]> GetProductsFromIds(IEnumerable<string> ids) {
        string[] idArr = ids.ToArray();
        DbSerbleProduct[] rows = await db.SerbleProducts
            .AsNoTracking()
            .Where(p => idArr.Contains(p.Id))
            .ToArrayAsync();
        Dictionary<string, SerbleProduct> byId = rows
            .Select(SerbleProduct.FromDb)
            .ToDictionary(p => p.Id, p => p);
        return idArr.Select(id => byId.TryGetValue(id, out SerbleProduct? p) ? p : null).ToArray();
    }

    public async Task<SerbleProduct[]> ListProductsOwnedBy(string userId) {
        string[] owned = await GetOwnedProducts(userId);
        SerbleProduct?[] prods = await GetProductsFromIds(owned);
        return prods.Where(p => p != null).Select(p => p!).ToArray();
    }

    public async Task<SerbleProduct[]> ListProducts() {
        DbSerbleProduct[] rows = await db.SerbleProducts.AsNoTracking().ToArrayAsync();
        return rows.Select(SerbleProduct.FromDb).ToArray();
    }

    public async Task<bool> CreateProduct(SerbleProduct product) {
        if (await db.SerbleProducts.AnyAsync(p => p.Id == product.Id)) return false;
        db.SerbleProducts.Add(ToDb(product));
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateProduct(SerbleProduct product) {
        DbSerbleProduct? row = await db.SerbleProducts.FirstOrDefaultAsync(p => p.Id == product.Id);
        if (row == null) return false;
        row.Name = product.Name;
        row.Description = product.Description;
        row.PriceIdsJson = JsonSerializer.Serialize(product.PriceIds ?? []);
        row.PriceLookupIdsJson = JsonSerializer.Serialize(product.PriceLookupIds ?? new Dictionary<string, string>());
        row.Purchasable = product.Purchasable;
        row.SuccessRedirect = product.SuccessRedirect;
        row.SuccessTokenSecret = product.SuccessTokenSecret;
        row.Webhook = product.Webhook;
        row.WebhookSecret = product.WebhookSecret;
        row.AllowAnonymous = product.AllowAnonymous;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteProduct(string id) {
        DbSerbleProduct? row = await db.SerbleProducts.FirstOrDefaultAsync(p => p.Id == id);
        if (row == null) return false;
        db.SerbleProducts.Remove(row);
        await db.SaveChangesAsync();
        return true;
    }

    private static DbSerbleProduct ToDb(SerbleProduct p) => new() {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        PriceIdsJson = JsonSerializer.Serialize(p.PriceIds ?? []),
        PriceLookupIdsJson = JsonSerializer.Serialize(p.PriceLookupIds ?? new Dictionary<string, string>()),
        Purchasable = p.Purchasable,
        SuccessRedirect = p.SuccessRedirect,
        SuccessTokenSecret = p.SuccessTokenSecret,
        Webhook = p.Webhook,
        WebhookSecret = p.WebhookSecret,
        AllowAnonymous = p.AllowAnonymous
    };
}
