using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class SerbleAuthorizationHeader {
    
    [FromHeader]
    // Format: "TYPE DATA"
    // App: "App base64()"
    // User: "User TOKEN"
    public string SerbleAuth { get; set; }

    /// <summary>
    /// Checks the header and authenticates the user.
    /// </summary>
    /// <param name="scopes">The authorized scopes</param>
    /// <param name="headerType">The type of header that was provided</param>
    /// <param name="authorizedObject">The object that was authorized, User for user auth and OAuthApp for app auth</param>
    /// <param name="msg">The reason why a return was made</param>
    /// <returns>Whether authentication was successful</returns>
    /// <exception cref="NotImplementedException">OAuth is not implemented yet</exception>
    public bool Check(out string? scopes, out SerbleAuthorizationHeaderType? headerType, out object? authorizedObject, out string? msg) {
        msg = null;
        scopes = "0";
        headerType = null;
        authorizedObject = null;

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
            
            case "App":
                headerType = SerbleAuthorizationHeaderType.App;
                throw new NotImplementedException("OAuth is not implemented");
            
            case "User":
                // Data is the token
                headerType = SerbleAuthorizationHeaderType.User;
                if (!TokenHandler.ValidateLoginToken(data, out User? user) || user == null) {
                    msg = "Invalid token";
                    return false;
                }
                authorizedObject = user;
                scopes = "1";  // 1 is full_access
                return true;
        }
        
    }
    
}

public enum SerbleAuthorizationHeaderType {
    App,
    User,
    Null
}