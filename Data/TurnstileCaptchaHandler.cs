using System.Text;
using GeneralPurposeLib;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data; 

public static class TurnstileCaptchaHandler {
    private const string Url = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    
    public static async Task<GoogleReCaptchaResponse> VerifyCaptcha(string token) {
        HttpClient client = new();
        dynamic body = new {
            secret = Program.Config!["turnstile_captcha_secret_key"],
            response = token
        };
        HttpResponseMessage response;
        try {
            response = await client.PostAsync(Url, new StringContent(JsonConvert.SerializeObject(body), Encoding.Default, "application/json"));
        }
        catch (Exception e) {
            Logger.Error("Error validating recaptcha: " + e);
            return new GoogleReCaptchaResponse(false);
        }
        
        string json = await response.Content.ReadAsStringAsync();
        Logger.Debug("Turnstile Response: " + json);
        GoogleReCaptchaResponse result = JsonConvert.DeserializeObject<GoogleReCaptchaResponse>(json)!;
        return result;
    }
}