using System.Security;
using GeneralPurposeLib;
using Newtonsoft.Json;
using SerbleAPI.Data.Schemas;
using JsonException = System.Text.Json.JsonException;

namespace SerbleAPI.Data.Storage;

/*
 * Dotnet's builtin JSON serializer does not seem to work with truples.
 * So I'm using Newtonsoft.Json.
 */

public class FileStorageService : IStorageService {

    private List<User> _users = new();
    private List<OAuthApp> _apps = new();
    // (userid, appid, scopes)
    private List<(string, AuthorizedApp)> _authorizations = new();
    private Dictionary<string, string> _kv = new();
    private List<(string, string)> _ownedProducts = new();  // (userid, productid)
    private List<(string, string, string)> _userNotes = new();  // (userid, noteid, note)

    public void Init() {
        _users = new List<User>();
        _apps = new List<OAuthApp>();
        _authorizations = new List<(string, AuthorizedApp)>();
        _kv = new Dictionary<string, string>();
        _ownedProducts = new List<(string, string)>();
        _userNotes = new List<(string, string, string)>();
        
        // Add dummy data
        _users.Add(new User {
            Id = Guid.NewGuid().ToString(),
            Username = "admin",
            PasswordHash = "e",
            PermLevel = 0
        });
        OAuthApp app = new (_users.First().Id) {
            Name = "Test App",
            Description = "Test App"
        };
        _apps.Add(app);

        Logger.Info("Loading data from data.json...");
        if (File.Exists("data.json")) {
            string jsonData = File.ReadAllText("data.json");
            (List<User>, List<OAuthApp>, List<(string, AuthorizedApp)>, Dictionary<string, string>, List<(string, string)>, List<(string, string, string)>) data = 
                JsonConvert.DeserializeObject<(List<User>, List<OAuthApp>, List<(string, AuthorizedApp)>, Dictionary<string, string>, List<(string, string)>, List<(string, string, string)>)>(jsonData);
            _users = data.Item1;
            _apps = data.Item2;
            _authorizations = data.Item3;
            _kv = data.Item4;
            _ownedProducts = data.Item5;
            _userNotes = data.Item6;
            Logger.Info("Loaded data from data.json");
        } else {
            Logger.Info("No data.json found, creating new data.json");
            File.WriteAllText("data.json", JsonConvert.SerializeObject((_users, _apps)));
            Logger.Info("Created new data.json");
        }
        Logger.Info("Data loaded");
    }

    public void Deinit() {
        bool retry = true;
        while (retry) {
            retry = false;
            bool error = false;
            string errorText = "Unspecified error";
            Logger.Info("Saving data to data.json...");
            try {
                File.WriteAllText("data.json", JsonConvert.SerializeObject((_users, _apps, _authorizations, _kv, _ownedProducts, _userNotes)));
                Logger.Info("Saved data to data.json");
            }
            catch (JsonException e) {
                Logger.Error($"Failed to save data to data.json: The data failed to serialize: {e.Message}");
                Logger.Error("----- Data will not be saved -----");
            } catch (IOException e) {
                errorText = $"Failed to save data to data.json (IOException): {e.Message}";
                error = true;
            } catch (UnauthorizedAccessException) {
                errorText =
                    $"Can't save data due to unauthorized access. Please make sure you have write access to: {Directory.GetCurrentDirectory()}";
                error = true;
            } catch (SecurityException) {
                errorText =
                    $"Can't save data due to unauthorized access. Please make sure you have access to: {Directory.GetCurrentDirectory()}";
                error = true;
            }

            if (!error) continue;
            Logger.Error(errorText);
            string? input = null;
            while (input == null) {
                Console.WriteLine("Would you like to retry? (y/n)");
                input = Console.ReadLine();
                if (input == null) continue;
                if (input.ToLower() == "y") {
                    retry = true;
                    Logger.Info("Reattempting to save data...");
                }
                else {
                    Logger.Info("----- Data will not be saved -----");
                }
            }
        }
    }

    public void AddUser(User userDetails, out User newUser) {
        newUser = userDetails;
        newUser.Id = Guid.NewGuid().ToString();
        _users.Add(newUser);
    }

    public void GetUser(string userId, out User? user) {
        user = _users.FirstOrDefault(u => u.Id == userId)!;
    }

    public void UpdateUser(User userDetails) {
        User? user = _users.FirstOrDefault(u => u.Id == userDetails.Id);
        if (user == null) return;
        int index = _users.IndexOf(user);
        _users[index] = userDetails;
    }

    public void DeleteUser(string userId) {
        _users.RemoveAll(u => u.Id == userId);
    }

    public void GetUserFromName(string userName, out User? user) {
        user = _users.FirstOrDefault(u => u.Username == userName);
    }

    public void CountUsers(out long userCount) {
        userCount = (long) _users.Count;
    }

    public void GetUserFromStripeCustomerId(string subscriptionId, out User? user) {
        user = _users.FirstOrDefault(u => u.StripeCustomerId == subscriptionId);
    }

    public void AddAuthorizedApp(string userId, AuthorizedApp app) {
        _authorizations.Add((userId, app));
    }

    public void GetAuthorizedApps(string userId, out AuthorizedApp[] apps) {
        try {
            apps = _authorizations.Where(a => a.Item1 == userId).Select(a => a.Item2).ToArray();
        }
        catch (ArgumentNullException) {
            Logger.Error("Authorization was null");
            apps = Array.Empty<AuthorizedApp>();
        }
    }

    public void DeleteAuthorizedApp(string userId, string appId) {
        _authorizations.RemoveAll(a => a.Item1 == userId && a.Item2.AppId == appId);
    }

    public void AddOAuthApp(OAuthApp app) {
        _apps.Add(app);
    }

    public void GetOAuthApp(string appId, out OAuthApp? app) {
        app = _apps.FirstOrDefault(a => a.Id == appId);
    }

    public void UpdateOAuthApp(OAuthApp app) {
        OAuthApp? appToUpdate = _apps.FirstOrDefault(a => a.Id == app.Id);
        if (appToUpdate == null) return;
        int index = _apps.IndexOf(appToUpdate);
        _apps[index] = app;
    }

    public void DeleteOAuthApp(string appId) {
        _apps.RemoveAll(a => a.Id == appId);
    }

    public void GetOAuthAppsFromUser(string userId, out OAuthApp[] apps) {
        apps = _apps.Where(a => a.OwnerId == userId).ToArray();
    }

    public void BasicKvSet(string key, string value) {
        _kv[key] = value;
    }

    public void BasicKvGet(string key, out string? value) {
        value = _kv.TryGetValue(key, out string? v) ? v : null;
    }

    public void GetOwnedProducts(string userId, out string[] products) {
        products = _ownedProducts.Where(p => p.Item1 == userId).Select(p => p.Item2).ToArray();
    }

    public void AddOwnedProducts(string userId, string[] productId) {
        _ownedProducts.AddRange(productId.Select(p => (userId, p)));
    }

    public void RemoveOwnedProduct(string userId, string productId) {
        _ownedProducts.RemoveAll(p => p.Item1 == userId && p.Item2 == productId);
    }

    public void GetUserNotes(string userId, out string[] noteIds) {
        noteIds = _userNotes.Where(n => n.Item1 == userId).Select(n => n.Item2).ToArray();
    }

    public void CreateUserNote(string userId, string noteId, string note) {
        _userNotes.Add((userId, noteId, note));
    }

    public void UpdateUserNoteContent(string userId, string noteId, string note) {
        _userNotes.RemoveAll(n => n.Item1 == userId && n.Item2 == noteId);
        _userNotes.Add((userId, noteId, note));
    }

    public void GetUserNoteContent(string userId, string noteId, out string? content) {
        (string, string, string)? r = _userNotes.FirstOrDefault(n => n.Item1 == userId && n.Item2 == noteId);
        content = r?.Item3;
    }

    public void DeleteUserNote(string userId, string noteId) {
        _userNotes.RemoveAll(n => n.Item1 == userId && n.Item2 == noteId);
    }

    public void CreatePasskey(SavedPasskey key) {
        throw new NotImplementedException();
    }

    public void GetUsersPasskeys(string userId, out SavedPasskey[] keys) {
        throw new NotImplementedException();
    }

    public void SetPasskeySignCount(byte[] credId, int val) {
        throw new NotImplementedException();
    }

    public void IncrementPasskeySignCount(string userId, byte[] credId) {
        throw new NotImplementedException();
    }

    public void GetUserIdFromPasskeyId(byte[] credId, out string? userId) {
        throw new NotImplementedException();
    }

    public void GetPasskey(byte[] credId, out SavedPasskey? key) {
        throw new NotImplementedException();
    }
}
