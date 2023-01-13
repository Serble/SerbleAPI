using System.Data;
using GeneralPurposeLib;
using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage;

public class MySqlStorageService : IStorageService {
    private string? _connectString;

    public void Init() {
        Logger.Info("Initialising MySQL...");
        _connectString = $"server={Program.Config!["mysql_ip"]};" +
                         $"userid={Program.Config["mysql_user"]};" +
                         $"password={Program.Config["mysql_password"]};" +
                         $"database={Program.Config["mysql_database"]}";
        Logger.Info("Creating tables...");
        CreateTables();
        Logger.Info("MySQL initialised.");
    }

    public void Deinit() {
        Logger.Info("MySQL de-initialised");
    }

    private void CreateTables() {
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_users(
                           id VARCHAR(64) primary key,
                           username VARCHAR(255),
                           email VARCHAR(64),
                           verifiedEmail BOOLEAN,
                           password VARCHAR(64),
                           permlevel INT,
                           permstring VARCHAR(64),
                           premiumLevel INT,
                           subscriptionId VARCHAR(32),
                           language VARCHAR(8))");
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_user_authorized_apps(
                           userid VARCHAR(64),
                           appid VARCHAR(64),
                           scopes VARCHAR(128))");
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_apps(" +
                           "ownerid VARCHAR(64), " +
                           "id VARCHAR(64), " +
                           "name VARCHAR(64), " +
                           "description VARCHAR(1024), " +
                           "clientsecret VARCHAR(64))");
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_kv(" +
                            "k VARCHAR(64)," +
                            "v VARCHAR(1024))");
    }

    private void SendMySqlStatement(string statement) {
        MySqlHelper.ExecuteNonQuery(_connectString!, statement);
    }

    public void AddUser(User userDetails, out User newUser) {
        userDetails.Id = Guid.NewGuid().ToString();
        MySqlHelper.ExecuteNonQuery(_connectString!, 
            "INSERT INTO serblesite_users(" +
            "id, username, email, verifiedEmail, password, permlevel, permstring, premiumLevel, subscriptionId, language) " +
            "VALUES(@id, @username, @email, @verifiedEmail, @password, @permlevel, @permstring, @premiumLevel, @subscriptionId, @language)",
            new MySqlParameter("@id", userDetails.Id),
            new MySqlParameter("@username", userDetails.Username),
            new MySqlParameter("@email", userDetails.Email),
            new MySqlParameter("@verifiedEmail", userDetails.VerifiedEmail),
            new MySqlParameter("@password", userDetails.PasswordHash),
            new MySqlParameter("@permlevel", userDetails.PermLevel),
            new MySqlParameter("@permstring", userDetails.PermString),
            new MySqlParameter("@premiumLevel", userDetails.PremiumLevel),
            new MySqlParameter("@subscriptionId", userDetails.StripeCustomerId),
            new MySqlParameter("@language", userDetails.Language));
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
            PremiumLevel = reader.GetInt32("premiumLevel"),
            StripeCustomerId = subId,
            Language = language
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
                                                    "premiumLevel=@premiumLevel, " +
                                                    "subscriptionId=@subscriptionId," +
                                                    "language=@language " +
                                                    "WHERE id=@id",
            new MySqlParameter("@username", userDetails.Username),
            new MySqlParameter("@email", userDetails.Email),
            new MySqlParameter("@verifiedEmail", userDetails.VerifiedEmail),
            new MySqlParameter("@password", userDetails.PasswordHash),
            new MySqlParameter("@permlevel", userDetails.PermLevel),
            new MySqlParameter("@permstring", userDetails.PermString),
            new MySqlParameter("@premiumLevel", userDetails.PremiumLevel),
            new MySqlParameter("@subscriptionId", userDetails.StripeCustomerId),
            new MySqlParameter("@id", userDetails.Id),
            new MySqlParameter("@language", userDetails.Language));
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

    public void AddAuthorizedApp(string userId, AuthorizedApp app) {
        MySqlHelper.ExecuteNonQuery(_connectString, "INSERT INTO serblesite_user_authorized_apps (userid, appid, scopes) " +
                                                    "VALUES (@userid, @appid, @scopes)",
            new MySqlParameter("@userid", userId),
            new MySqlParameter("@appid", app.AppId),
            new MySqlParameter("@scopes", app.Scopes));
    }

    public void GetAuthorizedApps(string userId, out AuthorizedApp[] apps) {
        using MySqlDataReader reader2 = MySqlHelper.ExecuteReader(_connectString, "SELECT appid, scopes FROM serblesite_user_authorized_apps WHERE userid=@userid",
            new MySqlParameter("@userid", userId));
        List<AuthorizedApp> authedApps = new ();
        while (reader2.Read()) {
            authedApps.Add(new AuthorizedApp(
                reader2.GetString("appid"),
                reader2.GetString("scopes")));
        }
        reader2.Close();
        apps = authedApps.ToArray();
    }

    public void DeleteAuthorizedApp(string userId, string appId) {
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_user_authorized_apps WHERE userid=@userid AND appid=@appid",
            new MySqlParameter("@userid", userId),
            new MySqlParameter("@appid", appId));
    }

    public void AddOAuthApp(OAuthApp app) {
        MySqlHelper.ExecuteNonQuery(_connectString, "INSERT INTO serblesite_apps (id, ownerid, name, description, clientsecret) " +
                                                    "VALUES (@id, @ownerid, @name, @description, @clientsecret)",
            new MySqlParameter("@id", app.Id),
            new MySqlParameter("@ownerid", app.OwnerId),
            new MySqlParameter("@name", app.Name),
            new MySqlParameter("@description", app.Description),
            new MySqlParameter("@clientsecret", app.ClientSecret));
    }

    public void GetOAuthApp(string appId, out OAuthApp? app) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_apps WHERE id=@id",
            new MySqlParameter("@id", appId));
        if (!reader.Read()) {
            app = null;
            return;
        }
        app = new OAuthApp(reader.GetString("ownerid")) {
            Id = reader.GetString("id"),
            Name = reader.GetString("name"),
            Description = reader.GetString("description"),
            ClientSecret = reader.GetString("clientsecret")
        };
        reader.Close();
    }

    public void UpdateOAuthApp(OAuthApp app) {
        MySqlHelper.ExecuteNonQuery(_connectString, "UPDATE serblesite_apps SET name=@name, description=@description, clientsecret=@clientsecret, ownerid=@ownerid WHERE id=@id",
            new MySqlParameter("@name", app.Name),
            new MySqlParameter("@ownerid", app.OwnerId),
            new MySqlParameter("@description", app.Description),
            new MySqlParameter("@clientsecret", app.ClientSecret),
            new MySqlParameter("@id", app.Id));
    }

    public void DeleteOAuthApp(string appId) {
        MySqlHelper.ExecuteNonQuery(_connectString, "DELETE FROM serblesite_apps WHERE id=@id",
            new MySqlParameter("@id", appId));
    }

    public void GetOAuthAppsFromUser(string userId, out OAuthApp[] apps) {
        using MySqlDataReader reader = MySqlHelper.ExecuteReader(_connectString, "SELECT * FROM serblesite_apps WHERE ownerid=@id",
            new MySqlParameter("@id", userId));
        List<OAuthApp> appsList = new ();
        while (reader.Read()) {
            appsList.Add(new OAuthApp(userId) {
                Id = reader.GetString("id"),
                Name = reader.GetString("name"),
                Description = reader.GetString("description"),
                ClientSecret = reader.GetString("clientsecret")
            });
        }
        reader.Close();
        apps = appsList.ToArray();
    }

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
