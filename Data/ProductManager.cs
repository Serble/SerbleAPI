using System.Text.Json;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Data;

public static class ProductManager {

    public static async Task<(string[] lookupKeys, List<SerbleProduct> products)>
        CheckoutBodyToLookupIds(JsonDocument doc, IProductRepository productRepo) {
        JsonElement root = doc.RootElement;
        List<string> ids = [];
        List<SerbleProduct> products = [];
        foreach (JsonElement prod in root.EnumerateArray()) {
            if (prod.ValueKind == JsonValueKind.String) {
                // Old format: bare product id string.
                SerbleProduct? productOld = await productRepo.GetProductFromId(prod.GetString()!);
                if (productOld == null) {
                    continue;
                }
                string oldLookupId = productOld.PriceLookupIds.First().Value;
                if (string.IsNullOrEmpty(oldLookupId)) {
                    throw new InvalidOperationException($"Product '{productOld.Id}' has an empty price lookup key.");
                }
                ids.Add(oldLookupId);
                products.Add(productOld);
                continue;
            }

            string itemId = prod.GetProperty("id").GetString()!;

            SerbleProduct? product = await productRepo.GetProductFromId(itemId);
            if (product == null) {
                throw new KeyNotFoundException(itemId);
            }

            // Optional priceid — value in the request is the plan key (e.g. "monthly"),
            // not the Stripe lookup key itself.
            string priceId = (prod.TryGetProperty("priceid", out JsonElement priceIdElement) ? priceIdElement.GetString() : null) ??
                              product.PriceLookupIds.First().Key;
            if (!product.PriceLookupIds.TryGetValue(priceId, out string? lookupId) || string.IsNullOrEmpty(lookupId)) {
                throw new KeyNotFoundException($"{itemId}/{priceId}");
            }
            ids.Add(lookupId);
            products.Add(product);
        }
        return (ids.ToArray(), products);
    }

}