namespace SerbleAPI.Data.Schemas; 

public class AuthorizedApp {
    public string AppId { get; }
    public string Scopes { get; }
    
    public AuthorizedApp(string appId, string scopes) {
        AppId = appId;
        Scopes = scopes;
    }

    public static bool operator ==(AuthorizedApp a1, AuthorizedApp a2) {
        return a1.AppId == a2.AppId && a1.Scopes == a2.Scopes;
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
        return HashCode.Combine(AppId, Scopes);
    }
    
}