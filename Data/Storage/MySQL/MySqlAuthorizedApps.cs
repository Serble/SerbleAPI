using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    
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
    
}