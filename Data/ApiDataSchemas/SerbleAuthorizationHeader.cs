using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class SerbleAuthorizationHeader {
    
    [FromHeader]
    // Format: "TYPE DATA"
    // App: "App ACCESS_TOKEN"
    // User: "User TOKEN"
    public string SerbleAuth { get; set; } = null!;

    /// <summary>
    /// Checks the header and authenticates the user.
    /// Out objects should only be accessed if the function returns true.
    /// </summary>
    /// <param name="scopes">The authorized scopes</param>
    /// <param name="headerType">The type of header that was provided</param>
    /// <param name="msg">The reason why a return was made</param>
    /// <param name="target">The user whose account the current app has access to</param>
    /// <returns>Whether authentication was successful</returns>
    /// <exception cref="NotImplementedException">OAuth is not implemented yet</exception>
    public bool Check(out string scopes, out SerbleAuthorizationHeaderType? headerType, out string msg, out User target) {
        msg = null!;
        target = null!;
        scopes = "0";
        headerType = null;

        if (string.IsNullOrEmpty(SerbleAuth)) {
            msg = "Authorization header is missing";
            return false;
        }

        string[] parts = SerbleAuth.Split(' ');
        if (parts.Length != 2) {
            msg = "Header is not in the correct format (TYPE DATA)";
            return false;
        }

        string type = parts[0];
        string data = parts[1];

        switch (type) {
            default:
                msg = "Header type is not supported";
                return false;
            
            // App auth
            case "App":
                headerType = SerbleAuthorizationHeaderType.App;
                if (!TokenHandler.ValidateAccessToken(data, out User? appUser, out string scope)) {
                    msg = "Access token is invalid";
                    return false;
                }
                target = appUser!;
                scopes = scope;
                return true;
            
            // User auth
            case "User":
                // Data is the token
                headerType = SerbleAuthorizationHeaderType.User;
                if (!TokenHandler.ValidateLoginToken(data, out User? user) || user == null) {
                    msg = "Invalid token";
                    return false;
                }
                target = user;
                scopes = "1";  // 1 is full_access
                return true;
        }
        
    }

    public bool CheckAndGetInfo(out User user,
        out Dictionary<string, string> userTranslations,
        ScopeHandler.ScopesEnum? requiredScope = null,
        bool allowApps = true,
        HttpRequest? request = null) {
        return CheckAndGetInfo(out user, out userTranslations, out SerbleAuthorizationHeaderType _, out string _, requiredScope, allowApps, request);
    }
    
    public bool CheckAndGetInfo(out User user, 
        out Dictionary<string, string> userTranslations, 
        out SerbleAuthorizationHeaderType type,
        out string scopes,
        ScopeHandler.ScopesEnum? requiredScope = null, 
        bool allowApps = true, 
        HttpRequest? request = null) {
        
        // Init defaults
        user = null!;
        userTranslations = null!;
        type = SerbleAuthorizationHeaderType.Null;
        
        if (!Check(out scopes, out SerbleAuthorizationHeaderType? gottenType, out string _, out User target)) {
            return false;
        }
        type = gottenType!.Value;
        if (type == SerbleAuthorizationHeaderType.App && !allowApps) {
            return false;
        }
        if (requiredScope != null && !scopes.SerbleHasScope(requiredScope.Value)) {
            return false;
        }
        user = target;
        userTranslations = LocalisationHandler.GetTranslations(LocalisationHandler.GetPreferredLanguageOrDefault(request, target));
        return true;
    }
    
}

public enum SerbleAuthorizationHeaderType {
    App,
    User,
    Null
}