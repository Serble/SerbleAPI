using GeneralPurposeLib;
using MySql.Data.MySqlClient;

namespace SerbleAPI.Data.Storage.MySQL;

public partial class MySqlStorageService : IStorageService {
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
                           "clientsecret VARCHAR(64), " + 
                           "redirecturi TEXT)");
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_kv(" +
                            "k VARCHAR(64)," +
                            "v VARCHAR(1024))");
        SendMySqlStatement(@"CREATE TABLE IF NOT EXISTS serblesite_owned_products(" +
                           "user VARCHAR(64), " +
                           "product VARCHAR(64), " +
                           "FOREIGN KEY (user) REFERENCES serblesite_users(id))");
    }

    private void SendMySqlStatement(string statement) {
        MySqlHelper.ExecuteNonQuery(_connectString!, statement);
    }
    
}
