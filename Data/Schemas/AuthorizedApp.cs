namespace SerbleAPI.Data.Schemas; 

/// <summary>
/// Which authorization flow produced a grant. The two describe permissions differently, so
/// <see cref="AuthorizedApp.Scopes"/> cannot be read without knowing which this is.
/// </summary>
public enum AuthorizedAppGrantType {
    /// <summary>The Serble OAuth flow. Scopes are a bitmask string over <see cref="ScopeHandler.Scopes"/>.</summary>
    Legacy = 0,
    /// <summary>The OpenID Connect flow. Scopes are a space-delimited list of <see cref="OidcScopes"/> names.</summary>
    Oidc = 1
}

public class AuthorizedApp {
    public string AppId { get; }

    /// <summary>Raw stored value, in the notation <see cref="GrantType"/> names; prefer <see cref="ScopeIds"/>.</summary>
    public string Scopes { get; }

    /// <summary>Which flow granted this. Defaults to <see cref="AuthorizedAppGrantType.Legacy"/>.</summary>
    public AuthorizedAppGrantType GrantType { get; set; }

    public DateTime DateCreated { get; set; }

    /// <summary>The granted scopes as identifiers, whichever flow they came from.</summary>
    public string[] ScopeIds => GrantType switch {
        AuthorizedAppGrantType.Oidc => OidcScopes.Parse(Scopes),
        _                           => ScopeHandler.StringToListOfScopeIds(Scopes)
    };

    public AuthorizedApp(string appId, string scopes) {
        AppId = appId;
        Scopes = scopes;
        DateCreated = DateTime.UtcNow;
    }

    public static bool operator ==(AuthorizedApp a1, AuthorizedApp a2) {
        return a1.AppId == a2.AppId && a1.Scopes == a2.Scopes && a1.GrantType == a2.GrantType;
    }
    
    public static bool operator !=(AuthorizedApp a1, AuthorizedApp a2) {
        return !(a1 == a2);
    }
    
    public override bool Equals(object? obj) {
        if (obj is AuthorizedApp app) {
            return this == app;
        }
        return false;
    }

    protected bool Equals(AuthorizedApp other) {
        return this == other;
    }
    
    public override int GetHashCode() {
        return HashCode.Combine(AppId, Scopes, GrantType);
    }
    
}
