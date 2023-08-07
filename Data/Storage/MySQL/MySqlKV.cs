using MySql.Data.MySqlClient;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    
    public void BasicKvSet(string key, string value) {
        MySqlHelper.ExecuteNonQuery(_connectString, "INSERT INTO serblesite_kv (k, v) VALUES (@key, @value) ON DUPLICATE KEY UPDATE v=@value",
            new MySqlParameter("@key", key),
            new MySqlParameter("@value", value));
    }

    public void BasicKvGet(string key, out string? value) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT v FROM serblesite_kv WHERE k=@key",
            new MySqlParameter("@key", key));
        if (!reader.Read()) {
            value = null;
            return;
        }
        value = reader.GetString("v");
        reader.Close();
    }
    
}