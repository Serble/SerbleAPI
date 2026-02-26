using System.Text;
using OtpNet;
using QRCoder;
using SerbleAPI.Repositories;
using Stripe;

namespace SerbleAPI.Data.Schemas; 

public class User {
    
    public string Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool VerifiedEmail { get; set; }
    /// <summary>
    /// Password + Salt, unless they registered before this was added then (salt is null): Password
    /// </summary>
    public string PasswordHash { get; set; }
    /// <summary>
    /// 0=Disabled Account 1=Normal, 2=Admin
    /// </summary>
    public int PermLevel { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? Language { get; set; }
    public bool TotpEnabled { get; set; }
    public string? TotpSecret { get; set; }  // 128 bytes
    public string? PasswordSalt { get; set; }  // 64 bytes, null for people who registered before this was added

    private AuthorizedApp[]? _obtainedAuthedApps;
    private AuthorizedApp[]? _originalAuthedApps;

    public async Task<IEnumerable<string>> GetAuthorizedAppIds() {
        return (await GetAuthorizedApps()).Select(x => x.AppId).ToArray();
    }
    
    public User() {
        Id = "";
        Username = "";
        Email = "";
        PasswordHash = "";
        PermLevel = 0;
        Language = "eng";
        VerifiedEmail = false;
        TotpEnabled = false;
        _originalAuthedApps = [];
        StripeCustomerId = null;
    }
    
    public async Task<AuthorizedApp[]> GetAuthorizedApps() {
        if (_obtainedAuthedApps != null) return _obtainedAuthedApps;
        await ObtainAuthorizedApps();
        return _obtainedAuthedApps!;
    }
    
    public bool CheckPassword(string password) {
        return PasswordHash == (password + (PasswordSalt ?? "")).Sha256Hash();
    }
    
    /// <summary>
    /// Must be set before calling any method that touches storage (ObtainAuthorizedApps,
    /// AuthorizeApp, EnsureStripeCustomer, RegisterChanges, UpdateAuthorizedApps).
    /// Controllers should call user.WithRepos(userRepo) after loading the user.
    /// </summary>
    private IUserRepository? _userRepo;

    public User WithRepos(IUserRepository userRepo) {
        _userRepo = userRepo;
        return this;
    }

    public async Task ObtainAuthorizedApps() {
        _originalAuthedApps = await _userRepo!.GetAuthorizedApps(Id);
        _obtainedAuthedApps = _originalAuthedApps;
    }

    public Task AuthorizeApp(string appId, string scopes) {
        AuthorizedApp app = new(appId, scopes);
        return AuthorizeApp(app);
    }

    public async Task AuthorizeApp(AuthorizedApp app) {
        // If the app is already authorised, delete it first
        AuthorizedApp[] authedApps = await GetAuthorizedApps();
        foreach (AuthorizedApp authedApp in authedApps.Where(oa => oa.AppId == app.AppId)) {
            await _userRepo!.DeleteAuthorizedApp(Id, authedApp.AppId);
        }
        await _userRepo!.AddAuthorizedApp(Id, app);
    }
    
    public async Task EnsureStripeCustomer() {
        if (StripeCustomerId != null) return;
        CustomerCreateOptions options = new() {
            Name = Username
        };
        if (VerifiedEmail) {
            options.Email = Email;
        }
        CustomerService service = new();
        Customer customer = await service.CreateAsync(options);
        StripeCustomerId = customer.Id;
        await RegisterChanges();
    }

    public async Task RegisterChanges() {
        await _userRepo!.UpdateUser(this);
        await UpdateAuthorizedApps();
    }
    
    public async Task UpdateAuthorizedApps() {
        if (_originalAuthedApps == null || _obtainedAuthedApps == null) {
            return;
        }
        
        // Find out which apps were added/removed
        AuthorizedApp[] addedApps = _obtainedAuthedApps.Except(_originalAuthedApps).ToArray();
        AuthorizedApp[] removedApps = _originalAuthedApps.Except(_obtainedAuthedApps).ToArray();
        
        // Remove the removed apps
        foreach (AuthorizedApp app in removedApps) {
            await _userRepo!.DeleteAuthorizedApp(Id, app.AppId);
        }
        
        // Add the new apps
        foreach (AuthorizedApp app in addedApps) {
            await _userRepo!.AddAuthorizedApp(Id, app);
        }
    }
    
    public async Task<bool> ValidateTotp(string code) {
        if (TotpSecret == null) {
            TotpSecret = SerbleUtils.RandomString(128);
            await RegisterChanges();
        }
        
        byte[] secretBytes = Encoding.UTF8.GetBytes(TotpSecret);
        Totp totp = new(secretBytes);
        return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
    }

    public async Task<byte[]?> GetTotpQrCode() {
        if (TotpSecret == null) {
            TotpSecret = SerbleUtils.RandomString(128);
            await RegisterChanges();
        }
        string uriString = GetTotpUri();
        QRCodeGenerator qrGenerator = new();
        QRCodeData qrCodeData = qrGenerator.CreateQrCode(uriString, QRCodeGenerator.ECCLevel.Q);
        BitmapByteQRCode qrCode = new(qrCodeData);
        return qrCode.GetGraphic(1);
    }

    public string GetTotpUri() {
        return new OtpUri(OtpType.Totp, Encoding.UTF8.GetBytes(TotpSecret!), Username, "Serble").ToString()!;
    }

    public bool IsAdmin() => PermLevel == 2;

}