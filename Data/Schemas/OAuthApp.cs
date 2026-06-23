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

    public OAuthApp(string ownerId) {
        Name = "";
        Description = "";
        OwnerId = ownerId;
        Id = Guid.NewGuid().ToString();
        ClientSecret = Guid.NewGuid().ToString();
        RedirectUri = "";
        AdditionalRedirectUris = [];
    }
    
    public OAuthApp CycleClientSecret() {
        ClientSecret = Guid.NewGuid().ToString();
        return this;
    }

    /// <summary>Every registered redirect URI (legacy single + additional OIDC ones), de-duplicated.</summary>
    public IEnumerable<string> AllRedirectUris =>
        new[] { RedirectUri }
            .Concat(AdditionalRedirectUris)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct();

    /// <summary>Exact-match redirect URI validation (no prefix/wildcard/query stripping).</summary>
    public bool IsValidRedirectUri(string redirectUri) =>
        AllRedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal));

}