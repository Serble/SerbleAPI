namespace SerbleAPI.Data.Schemas; 

public class SanitisedUser {
    
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public bool VerifiedEmail { get; set; }

    // 0=Disabled Account 1=Normal, 2=Admin
    public int? PermLevel { get; set; }
    [Obsolete("PremiumLevel is no longer used. Replaced with products API.")]
    public int PremiumLevel { get; set; }
    public string? PermString { get; set; }
    public AuthorizedApp[]? AuthorizedApps { get; set; }
    public string? Language { get; set; }
    public bool TotpEnabled { get; set; }
    
    [Obsolete("Stripe Customer ID is no longer provided to clients for security reasons.")]
    public string? StripeCustomerId { get; set; }

    private SanitisedUser() {
        
    }

    public static async Task<SanitisedUser> Create(User user, string scopeString, bool ignoreAuthedApps = false) {
        SanitisedUser sanitisedUser = new();
        
        string[] scopes = ScopeHandler.StringToListOfScopeIds(scopeString);
        bool hasFullAccess = scopes.Contains("full_access");
        sanitisedUser.Id = user.Id;

        if (scopes.Contains("user_info") || hasFullAccess) {
            sanitisedUser.Username = user.Username;
            sanitisedUser.Email = user.Email;
            sanitisedUser.VerifiedEmail = user.VerifiedEmail;
            sanitisedUser.PermLevel = user.PermLevel;
            sanitisedUser.Language = user.Language;
        }

        if (scopes.Contains("manage_account") || hasFullAccess) {
            sanitisedUser.TotpEnabled = user.TotpEnabled;
        }

        if ((scopes.Contains("apps_control") || hasFullAccess) && !ignoreAuthedApps) {
            sanitisedUser.AuthorizedApps = await user.GetAuthorizedApps();
        }
        
        return sanitisedUser;
    }
}