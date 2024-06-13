using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage.MySQL;

public partial class MySqlStorageService {
    public void CreatePasskey(SavedPasskey key) {
        MySqlHelper.ExecuteNonQuery(_connectString!, 
            "INSERT INTO serblesite_user_passkeys(" +
            "ownerid, credentialid, publickey, signcount, aaguid) " +
            "VALUES(@ownerid, @credentialid, @publickey, @signcount, @aaguid)",
            new MySqlParameter("@ownerid", key.OwnerId),
            new MySqlParameter("@credentialid", key.CredentialId.Base64Encode()),
            new MySqlParameter("@publickey", key.PublicKey.Base64Encode()),
            new MySqlParameter("@signcount", key.SignCount),
            new MySqlParameter("@aaguid", key.AaGuid.Base64Encode()));
    }

    public void GetUsersPasskeys(string userId, out SavedPasskey[] keys) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_user_passkeys WHERE ownerid=@id",
            new MySqlParameter("@id", userId));
        if (!reader.Read()) {
            keys = [];
            return;
        }
        string? subId = reader.IsDBNull("subscriptionId") ? null : reader.GetString("subscriptionId");
        string? language = reader.IsDBNull("language") ? null : reader.GetString("language");
        user = new User {
            Id = reader.GetString("id"),
            Username = reader.GetString("username"),
            Email = reader.GetString("email"),
            VerifiedEmail = reader.GetBoolean("verifiedEmail"),
            PasswordHash = reader.GetString("password"),
            PermLevel = reader.GetInt32("permlevel"),
            PermString = reader.GetString("permstring"),
            StripeCustomerId = subId,
            Language = language,
            TotpEnabled = reader.GetBoolean("totp_enabled"),
            TotpSecret = reader.IsDBNull("totp_secret") ? null : reader.GetString("totp_secret"),
            PasswordSalt = reader.IsDBNull("password_salt") ? null : reader.GetString("password_salt")
        };

        reader.Close();
    }

    public void IncrementPasskeySignCount(string userId, byte[] credId) {
        throw new NotImplementedException();
    }
}