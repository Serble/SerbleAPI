using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AuthorizationHeaderUser {
    
    [FromHeader]
    // Format: "APPID SECRET"
    public string SerbleAuth { get; set; } = null!;

    public bool Check(out string? msg) {
        msg = null;
        
        if (string.IsNullOrEmpty(SerbleAuth)) {
            msg = "Authorization header is missing";
            return false;
        }

        string[] parts = SerbleAuth.Split(' ');
        if (parts.Length != 3) {
            msg = "Header is not in the correct format";
            return false;
        }

        string appId = parts[0];
        string secret = parts[1];

        // Find app
        Program.StorageService!.GetOAuthApp(appId, out OAuthApp? app);
        if (app == null) {
            msg = "App null";
            return false;
        }

        if (app.ClientSecret != secret) {
            msg = "Client secret is not correct";
            return false;
        }
        
        msg = "Check success";
        return true;
    }
    
}