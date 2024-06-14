using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.Storage; 

public interface IStorageService {
    public void Init();
    public void Deinit();

    public void AddUser(User userDetails, out User newUser);
    public void GetUser(string userId, out User? user);
    public void UpdateUser(User userDetails);
    public void DeleteUser(string userId);
    public void GetUserFromName(string userName, out User? user);
    public void CountUsers(out long userCount);
    public void GetUserFromStripeCustomerId(string subscriptionId, out User? user);

    public void AddAuthorizedApp(string userId, AuthorizedApp app);
    public void GetAuthorizedApps(string userId, out AuthorizedApp[] apps);
    public void DeleteAuthorizedApp(string userId, string appId);

    public void AddOAuthApp(OAuthApp app);
    public void GetOAuthApp(string appId, out OAuthApp? app);
    public void UpdateOAuthApp(OAuthApp app);
    public void DeleteOAuthApp(string appId);
    public void GetOAuthAppsFromUser(string userId, out OAuthApp[] apps);
    
    public void BasicKvSet(string key, string value);
    public void BasicKvGet(string key, out string? value);
    
    public void GetOwnedProducts(string userId, out string[] products);
    public void AddOwnedProducts(string userId, string[] productIds);
    public void RemoveOwnedProduct(string userId, string productId);
    
    public void GetUserNotes(string userId, out string[] noteIds);
    public void CreateUserNote(string userId, string noteId, string note);
    public void UpdateUserNoteContent(string userId, string noteId, string note);
    public void GetUserNoteContent(string userId, string noteId, out string? content);
    public void DeleteUserNote(string userId, string noteId);

    public void CreatePasskey(SavedPasskey key);
    public void GetUsersPasskeys(string userId, out SavedPasskey[] keys);
    public void SetPasskeySignCount(byte[] credId, int val);
    public void GetUserIdFromPasskeyId(byte[] credId, out string? userId);
    public void GetPasskey(byte[] credId, out SavedPasskey? key);
}