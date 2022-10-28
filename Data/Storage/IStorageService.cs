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
}