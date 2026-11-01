namespace SerbleAPI.Data.Schemas; 

// DO NOT REMOVE SETTERS OR MAKE THEM PRIVATE, IT BREAKS THE JSON SERIALIZATION
public class OAuthApp {
    public string OwnerId { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ClientSecret { get; set; }
    public string RedirectUri { get; set; }
    public List<string> AdditionalRedirectUris { get; set; }
    public bool IsPublicClient { get; set; }
    public bool RequirePkce { get; set; }
    public bool IsOfficial { get; set; }
    public DateTime DateCreated { get; set; }

    /// <summary>Which apps may sign users in to this one server to server. Owner-controlled.</summary>
    public CrossAppPolicy CrossAppPolicy { get; set; }
    /// <summary>If non-empty, only these caller app ids may (still subject to the policy).</summary>
    public List<string> CrossAppAllowedCallers { get; set; }
    /// <summary>
    /// Space-separated Serble permissions (legacy scope ids) this app receives through cross-app
    /// sign-in; null for the identity-only default. See <see cref="Data.CrossAppRules.OidcScopesFor"/>
    /// for what an OIDC redemption gets instead.
    /// </summary>
    public string? CrossAppScopes { get; set; }

    public OAuthApp(string ownerId) {
        Name = "";
        Description = "";
        OwnerId = ownerId;
        Id = Guid.NewGuid().ToString();
        ClientSecret = Guid.NewGuid().ToString();
        RedirectUri = "";
        AdditionalRedirectUris = [];
        CrossAppAllowedCallers = [];
        DateCreated = DateTime.UtcNow;
    }
    
    public OAuthApp CycleClientSecret() {
        ClientSecret = Guid.NewGuid().ToString();
        return this;
    }

    /// <summary>Every registered redirect URI (legacy single + additional OIDC ones), de-duplicated.</summary>
    public IEnumerable<string> AllRedirectUris =>
        RedirectUri.Split(';')
            .Concat(AdditionalRedirectUris)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct();

    /// <summary>Exact-match redirect URI validation (no prefix/wildcard/query stripping).</summary>
    public bool IsValidRedirectUri(string redirectUri) =>
        AllRedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal));

}