using System.Text.Json;
using Newtonsoft.Json;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using File = System.IO.File;

namespace SerbleAPI.Data; 

// TODO: Have this be a service and DI
// TODO: Load products from regular config not custom one
public static class ProductManager {
    private static SerbleProduct[] _products = null!;

    public static void Load() {
        if (!File.Exists("products.json")) {
            SerbleProduct[] examples = {
                new() {
                    Name = "Premium",
                    Description = "Premium features for Serble",
                    Id = "premium",
                    PriceIds = new[] {
                        "price_xxxxxxxxxxxxxxxxxxxxxxxx"
                    },
                    PriceLookupIds = new Dictionary<string, string> {
                        { "monthly", "premium_monthly" }
                    },
                    Purchasable = true
                }
            };
            string write = JsonConvert.SerializeObject(examples, Formatting.Indented);
            File.WriteAllText("products.json", write);
        }
        string json = File.ReadAllText("products.json");
        _products = JsonConvert.DeserializeObject<SerbleProduct[]>(json)!;
    }

    public static SerbleProduct? GetProductFromPriceId(string priceId) {
        return _products.FirstOrDefault(product => product.PriceIds.Contains(priceId));
    }
    
    public static SerbleProduct? GetProductFromId(string id) {
        return _products.FirstOrDefault(product => product.Id == id);
    }
    
    public static SerbleProduct?[] GetProductsFromIds(IEnumerable<string> ids) {
        return ids.Select(GetProductFromId).ToArray();
    }
    
    public static SerbleProduct[] ListOfProductsFromUser(User target, IProductRepository productRepo) {
        string[] products = productRepo.GetOwnedProducts(target.Id);
        return GetProductsFromIds(products).Where(product => product != null).Select(product => product!).ToArray();
    }

    public static string[] CheckoutBodyToLookupIds(JsonDocument doc, out List<SerbleProduct> products) {
        JsonElement root = doc.RootElement;
        List<string> ids = new();
        products = new List<SerbleProduct>();
        foreach (JsonElement prod in root.EnumerateArray()) {
            if (prod.ValueKind == JsonValueKind.String) {
                // Old format
                SerbleProduct? productOld = GetProductFromId(prod.GetString()!);
                if (productOld == null) {
                    continue;
                }
                ids.Add(productOld.PriceLookupIds.First().Value);
                products.Add(productOld);
                continue;
            }
            
            string itemId = prod.GetProperty("id").GetString()!;
            
            SerbleProduct? product = GetProductFromId(itemId);
            if (product == null) {
                throw new KeyNotFoundException(itemId);
            }
            
            // Optional priceid
            string priceId = (prod.TryGetProperty("priceid", out JsonElement priceIdElement) ? priceIdElement.GetString() : null) ??
                              product.PriceLookupIds.First().Value;
            ids.Add(product.PriceLookupIds[priceId]);
        }
        return ids.ToArray();
    }
    
}