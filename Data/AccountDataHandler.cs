using GeneralPurposeLib;
using Newtonsoft.Json;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

public static class AccountDataHandler {
    private const string AccountFile = "account.json";
    private const string AuthedAppsFile = "authedapps.json";
    private const string UserNotesFolder = "notes";
    private const string OwnedProdsFile = "ownedproducts.json";
    
    public static void ScheduleUserDataCollation(User user) {
        Task.Run(() => CollateUserData(user));
    }

    private static async Task CollateUserData(User user) {
        Logger.Debug("Collating user data for " + user.Username);
        string dir = GetDataDumpDirectory(user.Id);
        
        // Account
        string accountFile = J(dir, AccountFile);
        Logger.Debug($"Creating {accountFile}");
        await File.WriteAllTextAsync(accountFile, JsonConvert.SerializeObject(user, Formatting.Indented));
        
        // Authorized Apps
        string authedAppsFile = J(dir, AuthedAppsFile);
        Logger.Debug($"Creating {authedAppsFile}");
        await File.WriteAllTextAsync(authedAppsFile, JsonConvert.SerializeObject(user.AuthorizedApps, Formatting.Indented));
        
        // User Notes
        string userNotesDir = J(dir, UserNotesFolder);
        Logger.Debug($"Creating {userNotesDir}");
        Directory.CreateDirectory(userNotesDir);
        Program.StorageService!.GetUserNotes(user.Id, out string[] notes);
        foreach (string noteId in notes) {
            Program.StorageService!.GetUserNoteContent(user.Id, noteId, out string? content);
            if (content == null) {
                continue;
            }
            string noteFile = J(userNotesDir, noteId + ".txt");
            Logger.Debug($"Creating {noteFile}");
            await File.WriteAllTextAsync(noteFile, content);
        }
    }
    
    private static string GetDataDumpDirectory(string userId) {
        string dir = Path.Combine("UserDataDump", userId);
        if (!Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static string J(params string[] paths) => Path.Combine(paths);

}