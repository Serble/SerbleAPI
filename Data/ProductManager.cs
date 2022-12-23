using SerbleAPI.Data.Schemas;
using Stripe;

namespace SerbleAPI.Data; 

public static class ProductManager {

    public static SerbleProduct GetProductFromPriceId(string priceId) {
        if (priceId == Program.Config!["stripe_premium_sub_id"] || priceId == Program.Config["stripe_testing_premium_sub_id"]) {
            return SerbleProduct.Premium;
        }
        else {
            return SerbleProduct.Unknown;
        }
    }
    
    public static SerbleProduct[] ListOfProductsFromUser(User target) {
        if (string.IsNullOrWhiteSpace(target.StripeCustomerId)) {
            return Array.Empty<SerbleProduct>();
        }
        
        CustomerService customerService = new();
        
        Customer customer;
        try {
            customer = customerService.Get(target.StripeCustomerId);
        }
        catch (StripeException) {
            return Array.Empty<SerbleProduct>();
        }
        List<Product> products = (from subscription in customer.Subscriptions.Data where subscription.Status == "active" select subscription.Items.Data[0].Price.Product).ToList();
        return products.Select(product => GetProductFromPriceId(product.DefaultPriceId)).ToArray();
    }
    
}

public enum SerbleProduct {
    Premium,
    Unknown
}