using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AntiSpamProtection {
    
    [FromHeader]
    // Either:
    // ReCaptcha | recaptcha TOKEN
    // Testing Bypass | bypass testing
    // Or be logged in with a verified email
    public string SerbleAntiSpam { get; set; } = null!;

    public async Task<bool> Check(SerbleAuthorizationHeaderType authType = SerbleAuthorizationHeaderType.Null, User? user = null) {
        
        // If the user is logged in with a verified email address, then they are automatically verified
        if (user != null && authType == SerbleAuthorizationHeaderType.User && user.VerifiedEmail) {
            return true;
        }
        
        string[] split = SerbleAntiSpam.Split(' ');
        if (split.Length != 2) {
            return false;
        }

        switch (split[0].ToLower()) {
            
            default:
                return false;
            
            case "recaptcha":
                GoogleReCaptchaResponse response = await GoogleReCaptchaHandler.VerifyReCaptcha(split[1]);
                if (!response.Success) {
                    return false;
                }
                return response.Score >= 0.5;
            
            case "bypass":
                return split[1] switch {
                    "testing" => Program.Testing,
                    _ => false
                };
        }
    }
}