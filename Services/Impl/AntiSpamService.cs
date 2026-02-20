using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data.ApiDataSchemas;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Impl;

public class AntiSpamService(IOptions<ApiSettings> apiSettings, IGoogleReCaptchaService recaptcha, ITurnstileCaptchaService turnstile) : IAntiSpamService {
    
    public async Task<bool> Check(AntiSpamHeader header, HttpContext context, 
        SerbleAuthorizationHeaderType authType = SerbleAuthorizationHeaderType.Null, User? user = null) {
        
        // If the user is logged in with a verified email address, then they are automatically verified
        if (user != null && authType == SerbleAuthorizationHeaderType.User && user.VerifiedEmail) {
            return true;
        }
        
        string[] split = header.SerbleAntiSpam.Split(' ');
        if (split.Length != 2) {
            return false;
        }

        switch (split[0].ToLower()) {
            
            default:
                return false;

            case "recaptcha": {
                GoogleReCaptchaResponse response = await recaptcha.VerifyReCaptcha(split[1]);
                if (!response.Success) {
                    return false;
                }

                return response.Score >= 0.5;
            }

            case "turnstile": {
                GoogleReCaptchaResponse response = await turnstile.VerifyCaptcha(split[1]);
                return response.Success;
            }

            case "bypass":
                return split[1] switch {
                    "testing" => apiSettings.Value.AllowAntiSpamBypass,
                    _ => false
                };
        }
    }
}
