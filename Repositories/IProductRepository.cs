using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

public interface IProductRepository {
    Task<string[]> GetOwnedProducts(string userId);
    Task AddOwnedProducts(string userId, string[] productIds);
    Task RemoveOwnedProduct(string userId, string productId);

    Task<SerbleProduct?> GetProductFromId(string id);
    Task<SerbleProduct?> GetProductFromPriceId(string priceId);
    Task<SerbleProduct?[]> GetProductsFromIds(IEnumerable<string> ids);
    Task<SerbleProduct[]> ListProductsOwnedBy(string userId);

    Task<SerbleProduct[]> ListProducts();
    Task<bool> CreateProduct(SerbleProduct product);
    Task<bool> UpdateProduct(SerbleProduct product);
    Task<bool> DeleteProduct(string id);
}
