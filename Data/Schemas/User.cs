using GeneralPurposeLib;

namespace SerbleAPI.Data.Schemas; 

public class User {
    
    public string Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool VerifiedEmail { get; set; }
    public string PasswordHash { get; set; }
    
    // 0=Disabled Account 1=Normal, 2=Admin
    public int PermLevel { get; set; }
    public string PermString { get; set; }
    public int PremiumLevel { get; set; }
    public string? StripeCustomerId { get; set; }
    
    // (appId, scopes)
    public AuthorizedApp[] AuthorizedApps {
        get {
            Logger.Debug("Get was made on AuthorizedApps");
            if (_obtainedAuthedApps != null) return _obtainedAuthedApps;
            Logger.Debug("AuthorizedApps was null");
            ObtainAuthorizedApps();
            return _obtainedAuthedApps!;
        }
        set {
            if (_obtainedAuthedApps == null) ObtainAuthorizedApps();
            _obtainedAuthedApps = value;
        }
    }

    private AuthorizedApp[]? _obtainedAuthedApps;
    private AuthorizedApp[]? _originalAuthedApps;

    public IEnumerable<string> AuthorizedAppIds => AuthorizedApps.Select(x => x.AppId).ToArray();
    
    public User() {
        Id = "";
        Username = "";
        Email = "";
        PasswordHash = "";
        PermLevel = 0;
        PremiumLevel = 0;
        PermString = "";
        VerifiedEmail = false;
        _originalAuthedApps = Array.Empty<AuthorizedApp>();
        StripeCustomerId = null;
    }
    
    public bool CheckPassword(string password) {
        return PasswordHash == Utils.ToSHA256(password);
    }
    
    public void ObtainAuthorizedApps() {
        Program.StorageService!.GetAuthorizedApps(Id, out _originalAuthedApps);
        _obtainedAuthedApps = _originalAuthedApps;
        Logger.Debug($"Obtained Authorized Apps for {Username}");
    }

    public void AuthorizeApp(string appId, string scopes) {
        AuthorizedApp app = new (appId, scopes);
        AuthorizeApp(app);
    }

    public void AuthorizeApp(AuthorizedApp app) {
        // If the app is already authorized delete it first
        foreach (AuthorizedApp authedApp in AuthorizedApps.Where(oa => oa.AppId == app.AppId)) {
            Program.StorageService!.DeleteAuthorizedApp(Id, authedApp.AppId);
        }
        Program.StorageService!.AddAuthorizedApp(Id, app);
    }

    public void RegisterChanges() {
        Logger.Debug($"Registering changes to user: '{Username}' with id: '{Id}'");
        
        Program.StorageService!.UpdateUser(this);

        if (_originalAuthedApps == null || _obtainedAuthedApps == null) {
            Logger.Debug("No changes to authorized apps were made");
            return;
        }
        
        // Find out which apps were added/removed
        AuthorizedApp[] addedApps = _obtainedAuthedApps.Except(_originalAuthedApps).ToArray();
        AuthorizedApp[] removedApps = _originalAuthedApps.Except(_obtainedAuthedApps).ToArray();
        
        // Remove the removed apps
        foreach (AuthorizedApp app in removedApps) {
            Program.StorageService.DeleteAuthorizedApp(Id, app.AppId);
        }
        
        // Add the new apps
        foreach (AuthorizedApp app in addedApps) {
            Program.StorageService.AddAuthorizedApp(Id, app);
        }

        Logger.Debug("Added/Removed authed apps: " + addedApps.Length + "/" + removedApps.Length);
    }

}