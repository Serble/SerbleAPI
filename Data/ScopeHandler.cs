namespace SerbleAPI.Data; 

public static class ScopeHandler {
    
    // Scope Format
    // Scope will be a string of 1s and 0s where 1 is a granted scope and 0 is a denied scope.
    // In the following order (Format: INDEX. FULL NAME | IDENTIFIER)
    // 0. Full Access | full_access
    // 1. File Host Access | file_host
    
    public static readonly string[] Scopes = {
        "full_access",
        "file_host",
        "user_info",
        "apps_control"
    };
    
    public static readonly string[] ScopeNames = {
        "Full Account Access",
        "File Host",
        "Account Information",
        "Control Of Authorized Applications"
    };
    
    public enum ScopesEnum {
        FullAccess,
        FileHost,
        UserInfo,
        AppsControl
    }

    // id, name
    public static List<(string, string)> ScopeList => Scopes.Select((t, i) => (t, ScopeNames[i])).ToList();

    public static string[] ScopeDescriptions = {
        "Allows full access to the account.",
        "Allows access the file host.",
        "Allows access to the account's information (Eg. Username, Email).",
        "Allows control over authorized applications."
    };

    public static string ListOfScopeIdsToString(IEnumerable<string> scopeIds) {
        return Scopes.Aggregate("", (current, scope) => current + (scopeIds.Contains(scope) ? "1" : "0"));
    }
    
    // Convert list of scope ids to list of scope names
    public static IEnumerable<string> ListOfScopeIdsToScopeNames(IEnumerable<string> scopeIds) {
        return ScopeNames.Where((_, index) => scopeIds.Contains(Scopes[index]));
    }
    
    public static IEnumerable<string> FilterInvalidScopes(IEnumerable<string> scopes) {
        return scopes.Where(scope => Scopes.Contains(scope)).ToArray();
    }
    
    public static string[] StringToListOfScopeIds(string scopeString) {
        string?[] scopes = scopeString.Select((t, i) => t == '1' ? Scopes[i] : null).Where(t => t != null).ToArray();
        return scopes.Where(t => t != null).ToArray()!;
    }
    
    public static string GetDescriptionFromName(string name) {
        return ScopeDescriptions[Array.IndexOf(ScopeNames, name)];
    }

    public static IEnumerable<ScopesEnum> ScopesIdsToEnumArray(IEnumerable<string> scopes) {
        return scopes.Select(t => (ScopesEnum)Enum.Parse(typeof(ScopesEnum), t));
    }
    
    public static IEnumerable<string> ScopesEnumToIdsArray(IEnumerable<ScopesEnum> scopes) {
        return scopes.Select(t => t.ToString());
    }
    
}