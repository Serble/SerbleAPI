namespace SerbleAPI.Data; 

public static class ProductManager {

    public static SerbleProduct GetProductFromPriceId(string priceId) {
        if (priceId == Program.Config!["stripe_premium_sub_id"]) {
            return SerbleProduct.Premium;
        }
        else {
            return SerbleProduct.Unknown;
        }
    }
    
}

public enum SerbleProduct {
    Premium,
    Unknown
}