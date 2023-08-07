using MySql.Data.MySqlClient;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    
    public void GetOwnedProducts(string userId, out string[] products) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_owned_products WHERE user=@id",
            new MySqlParameter("@id", userId));
        List<string> productsList = new();
        
        while (reader.Read()) {
            productsList.Add(reader.GetString("product"));
        }

        reader.Close();
        products = productsList.ToArray();
    }

    public void AddOwnedProducts(string userId, string[] productIds) {
        string query = "INSERT INTO serblesite_owned_products (user, product) VALUES";
        List<MySqlParameter> parameters = new();
        for (int i = 0; i < productIds.Length; i++) {
            query += " (@user" + i + ", @product" + i + "),";
            parameters.Add(new MySqlParameter("@user" + i, userId));
            parameters.Add(new MySqlParameter("@product" + i, productIds[i]));
        }

        // Remove the last comma
        query = query[..^1];

        MySqlHelper.ExecuteNonQuery(_connectString, query, parameters.ToArray());
    }

    public void RemoveOwnedProduct(string userId, string productId) {
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_owned_products WHERE user=@user AND product=@product",
            new MySqlParameter("@user", userId),
            new MySqlParameter("@product", productId));
    }
}