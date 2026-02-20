namespace SerbleAPI.Repositories;

public interface IProductRepository {
    string[] GetOwnedProducts(string userId);
    void AddOwnedProducts(string userId, string[] productIds);
    void RemoveOwnedProduct(string userId, string productId);
}
