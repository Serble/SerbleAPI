using SerbleAPI.Repositories;
using Stripe;

namespace SerbleAPI.Data.Schemas; 

public class User {
    
    public string Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool VerifiedEmail { get; set; }
    /// <summary>
    /// 0=Disabled Account 1=Normal, 2=Admin
    /// </summary>
    public int PermLevel { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? Language { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? LastLogin { get; set; }

    /// <summary>
    /// See <see cref="Models.DbUser.TokensValidFrom"/>. Move it with
    /// <see cref="IUserRepository.RevokeTokensIssuedBefore"/>, never by saving this user: a copy
    /// loaded before a sign-out would write the old value back and restore the retired tokens.
    /// </summary>
    public DateTime? TokensValidFrom { get; set; }

    private AuthorizedApp[]? _obtainedAuthedApps;
    private AuthorizedApp[]? _originalAuthedApps;

    public async Task<IEnumerable<string>> GetAuthorizedAppIds() {
        return (await GetAuthorizedApps()).Select(x => x.AppId).ToArray();
    }
    
    public User() {
        Id = "";
        Username = "";
        Email = "";
        PermLevel = 0;
        Language = "eng";
        VerifiedEmail = false;
        _originalAuthedApps = [];
        StripeCustomerId = null;
    }
    
    public async Task<AuthorizedApp[]> GetAuthorizedApps() {
        if (_obtainedAuthedApps != null) return _obtainedAuthedApps;
        await ObtainAuthorizedApps();
        return _obtainedAuthedApps!;
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
    
    public Task<bool> IsTotpInUse() => _userRepo!.IsTotpInUse(Id);

    public bool IsAdmin() => PermLevel == 2;

    /// <summary>
    /// Whether the account is disabled (<see cref="PermLevel"/> 0).
    /// <para>
    /// Every path that authenticates someone, and the authentication handler that accepts an
    /// existing token, checks this. A disabled account otherwise kept working entirely: the flag was
    /// written by the admin disable route and then read nowhere on the login or token paths, so the
    /// account could still log in for fresh tokens and every token it already held stayed valid.
    /// One definition, so no caller can disagree about what disabled means.
    /// </para>
    /// </summary>
    public bool IsDisabled() => PermLevel == 0;

}