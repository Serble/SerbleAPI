namespace SerbleAPI.Repositories;

public interface IProductRepository {
    Task<string[]> GetOwnedProducts(string userId);
    Task AddOwnedProducts(string userId, string[] productIds);
    Task RemoveOwnedProduct(string userId, string productId);
}
