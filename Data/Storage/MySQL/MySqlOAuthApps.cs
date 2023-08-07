using MySql.Data.MySqlClient;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage.MySQL; 

public partial class MySqlStorageService {
    
    public void AddOAuthApp(OAuthApp app) {
        MySqlHelper.ExecuteNonQuery(_connectString, "INSERT INTO serblesite_apps (id, ownerid, name, description, clientsecret, redirecturi) " +
                                                    "VALUES (@id, @ownerid, @name, @description, @clientsecret, @redirecturi)",
            new MySqlParameter("@id", app.Id),
            new MySqlParameter("@ownerid", app.OwnerId),
            new MySqlParameter("@name", app.Name),
            new MySqlParameter("@description", app.Description),
            new MySqlParameter("@clientsecret", app.ClientSecret),
            new MySqlParameter("@redirecturi", app.RedirectUri));
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
            ClientSecret = reader.GetString("clientsecret"),
            RedirectUri = reader.GetString("redirecturi")
        };
        reader.Close();
    }

    public void UpdateOAuthApp(OAuthApp app) {
        MySqlHelper.ExecuteNonQuery(_connectString, "UPDATE serblesite_apps SET name=@name, description=@description, clientsecret=@clientsecret, ownerid=@ownerid, redirecturi=@redirecturi WHERE id=@id",
            new MySqlParameter("@name", app.Name),
            new MySqlParameter("@ownerid", app.OwnerId),
            new MySqlParameter("@description", app.Description),
            new MySqlParameter("@clientsecret", app.ClientSecret),
            new MySqlParameter("@id", app.Id),
            new MySqlParameter("@redirecturi", app.RedirectUri));
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
                ClientSecret = reader.GetString("clientsecret"),
                RedirectUri = reader.GetString("redirecturi")
            });
        }
        reader.Close();
        apps = appsList.ToArray();
    }
    
}