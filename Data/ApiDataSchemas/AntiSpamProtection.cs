using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AntiSpamProtection {
    
    [FromHeader]
    // Either:
    // ReCaptcha | recaptcha TOKEN
    // Or be logged in with a verified email
    public string SerbleAntiSpam { get; set; }

    public async Task<bool> Check(SerbleAuthorizationHeaderType authType = SerbleAuthorizationHeaderType.Null, User? user = null) {
        if (user != null && authType == SerbleAuthorizationHeaderType.User && /* TODO: The users email is verified */ false) {
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
            
            case "email":
                // Not implemented
                return false;
        }
    }
}