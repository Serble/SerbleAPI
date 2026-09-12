namespace SerbleAPI.Data.Schemas;

// DO NOT REMOVE SETTERS OR MAKE THEM PRIVATE, IT BREAKS THE JSON SERIALIZATION
/// <summary>
/// An app as its owner sees it while managing it: every field of <see cref="OAuthApp"/> except
/// <see cref="OAuthApp.ClientSecret"/>.
/// <para>
/// The secret is deliberately absent. Routes that hand back the whole entity disclose it to
/// whatever token made the call, which means a scope that was only meant to let an app rename
/// itself also reads the credential that impersonates it. Anything that needs the secret asks for
/// it explicitly through a route gated on the app-management scope.
/// </para>
/// </summary>
public class OwnedOAuthApp {
    public string OwnerId { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string RedirectUri { get; set; }
    public List<string> AdditionalRedirectUris { get; set; }
    public bool IsPublicClient { get; set; }
    public bool RequirePkce { get; set; }
    public bool IsOfficial { get; set; }
    public DateTime DateCreated { get; set; }

    public OwnedOAuthApp(OAuthApp app) {
        OwnerId                = app.OwnerId;
        Id                     = app.Id;
        Name                   = app.Name;
        Description            = app.Description;
        RedirectUri            = app.RedirectUri;
        AdditionalRedirectUris = app.AdditionalRedirectUris;
        IsPublicClient         = app.IsPublicClient;
        RequirePkce            = app.RequirePkce;
        IsOfficial             = app.IsOfficial;
        DateCreated            = app.DateCreated;
    }
}
