namespace SerbleAPI.Data.Schemas; 

public class SanitisedUser {
    
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public bool VerifiedEmail { get; set; }

    // 0=Disabled Account 1=Normal, 2=Admin
    public int? PermLevel { get; set; }
    public int PremiumLevel { get; set; }
    public string? PermString { get; set; }
    public string? StripeCustomerId { get; set; }
    public AuthorizedApp[]? AuthorizedApps { get; set; }

    public SanitisedUser(User user, string scopeString, bool ignoreAuthedApps = false) {
        string[] scopes = ScopeHandler.StringToListOfScopeIds(scopeString);
        bool hasFullAccess = scopes.Contains("full_access");
        Id = user.Id;

        if (scopes.Contains("user_info") || hasFullAccess) {
            Username = user.Username;
            Email = user.Email;
            VerifiedEmail = user.VerifiedEmail;
            PermLevel = user.PermLevel;
            PremiumLevel = user.PremiumLevel;
        }

        if ((scopes.Contains("apps_control") || hasFullAccess) && !ignoreAuthedApps) {
            AuthorizedApps = user.AuthorizedApps;
        }

        if (hasFullAccess) {
            StripeCustomerId = user.StripeCustomerId;
        }
    }

}