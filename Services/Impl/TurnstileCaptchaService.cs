using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Impl; 

public class TurnstileCaptchaService(ILogger<TurnstileCaptchaService> logger, IOptions<TurnstileSettings> settings) : ITurnstileCaptchaService {
    private const string Url = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    
    public async Task<GoogleReCaptchaResponse> VerifyCaptcha(string token) {
        HttpClient client = new();
        dynamic body = new {
            secret = settings.Value.SecretKey,
            response = token
        };
        HttpResponseMessage response;
        try {
            response = await client.PostAsync(Url, new StringContent(JsonConvert.SerializeObject(body), Encoding.Default, "application/json"));
        }
        catch (Exception e) {
            logger.LogError("Error validating recaptcha: " + e);
            return new GoogleReCaptchaResponse(false);
        }
        
        string json = await response.Content.ReadAsStringAsync();
        logger.LogDebug("Turnstile Response: " + json);
        GoogleReCaptchaResponse result = JsonConvert.DeserializeObject<GoogleReCaptchaResponse>(json)!;
        return result;
    }
}
