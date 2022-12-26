using GeneralPurposeLib;
using Stripe;

namespace SerbleAPI.Data.Schemas; 

public class User {
    
    public string Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool VerifiedEmail { get; set; }
    public string PasswordHash { get; set; }
    /// <summary>
    /// 0=Disabled Account 1=Normal, 2=Admin
    /// </summary>
    public int PermLevel { get; set; }
    public string PermString { get; set; }
    /// <summary>
    /// 0=None 10=Premium
    /// </summary>
    public int PremiumLevel { get; set; }
    public string? StripeCustomerId { get; set; }
    
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
    
    public void EnsureStripeCustomer() {
        if (StripeCustomerId != null) return;
        CustomerCreateOptions options = new() {
            Email = Email,
            Name = Username
        };
        CustomerService service = new();
        Customer customer = service.Create(options);
        StripeCustomerId = customer.Id;
        RegisterChanges();
    }

    public void RegisterChanges() {
        Logger.Debug($"Registering changes to user: '{Username}' with id: '{Id}'");
        
        Program.StorageService!.UpdateUser(this);

        UpdateAuthorizedApps();
    }
    
    public void UpdateAuthorizedApps() {
        if (_originalAuthedApps == null || _obtainedAuthedApps == null) {
            Logger.Debug("No changes to authorized apps were made");
            return;
        }
        
        // Find out which apps were added/removed
        AuthorizedApp[] addedApps = _obtainedAuthedApps.Except(_originalAuthedApps).ToArray();
        AuthorizedApp[] removedApps = _originalAuthedApps.Except(_obtainedAuthedApps).ToArray();
        
        // Remove the removed apps
        foreach (AuthorizedApp app in removedApps) {
            Program.StorageService!.DeleteAuthorizedApp(Id, app.AppId);
        }
        
        // Add the new apps
        foreach (AuthorizedApp app in addedApps) {
            Program.StorageService!.AddAuthorizedApp(Id, app);
        }

        Logger.Debug("Added/Removed authed apps: " + addedApps.Length + "/" + removedApps.Length);
    }

    public bool IsAdmin() => PermLevel == 2;
    public bool IsPremium() => PremiumLevel == 10;

    }