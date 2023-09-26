using System.Data;
using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    public void AddUser(User userDetails, out User newUser) {
        userDetails.Id = Guid.NewGuid().ToString();
        MySqlHelper.ExecuteNonQuery(_connectString!, 
            "INSERT INTO serblesite_users(" +
            "id, username, email, verifiedEmail, password, permlevel, permstring, subscriptionId, language, totp_enabled, totp_secret, password_salt) " +
            "VALUES(@id, @username, @email, @verifiedEmail, @password, @permlevel, @permstring, @subscriptionId, @language, @topt_enabled, @topt_secret, @password_salt)",
            new MySqlParameter("@id", userDetails.Id),
            new MySqlParameter("@username", userDetails.Username),
            new MySqlParameter("@email", userDetails.Email),
            new MySqlParameter("@verifiedEmail", userDetails.VerifiedEmail),
            new MySqlParameter("@password", userDetails.PasswordHash),
            new MySqlParameter("@permlevel", userDetails.PermLevel),
            new MySqlParameter("@permstring", userDetails.PermString),
            new MySqlParameter("@subscriptionId", userDetails.StripeCustomerId),
            new MySqlParameter("@language", userDetails.Language),
            new MySqlParameter("@topt_enabled", userDetails.TotpEnabled),
            new MySqlParameter("@topt_secret", userDetails.TotpSecret),
            new MySqlParameter("@password_salt", userDetails.PasswordSalt));
        newUser = userDetails;
    }

    public void GetUser(string userId, out User? user) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_users WHERE id=@id",
            new MySqlParameter("@id", userId));
        if (!reader.Read()) {
            user = null;
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

    public void GetUserFromStripeCustomerId(string subId, out User? user) {
        user = null;
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_users WHERE subscriptionId=@subId",
            new MySqlParameter("@subId", subId));
        if (!reader.Read()) {
            return;
        }
        string userId = reader.GetString("id");
        reader.Close();
        GetUser(userId, out user);
    }

    public void UpdateUser(User userDetails) {
        MySqlHelper.ExecuteNonQuery(_connectString, "UPDATE serblesite_users SET " +
                                                    "username=@username, " +
                                                    "email=@email, " +
                                                    "verifiedEmail=@verifiedEmail, " +
                                                    "password=@password, " +
                                                    "permlevel=@permlevel, " +
                                                    "permstring=@permstring, " +
                                                    "subscriptionId=@subscriptionId," +
                                                    "language=@language, " +
                                                    "totp_enabled=@totp_enabled, " +
                                                    "totp_secret=@totp_secret, " +
                                                    "password_salt=@password_salt " +
                                                    "WHERE id=@id",
            new MySqlParameter("@username", userDetails.Username),
            new MySqlParameter("@email", userDetails.Email),
            new MySqlParameter("@verifiedEmail", userDetails.VerifiedEmail),
            new MySqlParameter("@password", userDetails.PasswordHash),
            new MySqlParameter("@permlevel", userDetails.PermLevel),
            new MySqlParameter("@permstring", userDetails.PermString),
            new MySqlParameter("@subscriptionId", userDetails.StripeCustomerId),
            new MySqlParameter("@id", userDetails.Id),
            new MySqlParameter("@language", userDetails.Language),
            new MySqlParameter("@totp_enabled", userDetails.TotpEnabled),
            new MySqlParameter("@totp_secret", userDetails.TotpSecret),
            new MySqlParameter("@password_salt", userDetails.PasswordSalt));
    }

    public void DeleteUser(string userId) {
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_users WHERE id=@id",
            new MySqlParameter("@id", userId));
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_user_authorized_apps WHERE userid=@id",
            new MySqlParameter("@id", userId));
    }

    public void GetUserFromName(string userName, out User? user) {
        user = null;
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT id FROM serblesite_users WHERE username=@username",
            new MySqlParameter("@username", userName));
        if (!reader.Read()) {
            return;
        }
        string userId = reader.GetString("id");
        reader.Close();
        GetUser(userId, out user);
    }

    public void CountUsers(out long userCount) {
        userCount = (long) MySqlHelper.ExecuteScalar(_connectString, "SELECT COUNT(*) FROM serblesite_users");
    }
}