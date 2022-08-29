using GeneralPurposeLib;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data; 

public static class GoogleReCaptchaHandler {
    
    public static async Task<GoogleReCaptchaResponse> VerifyReCaptcha(string token) {
        HttpClient client = new();
        string url = "https://www.google.com/recaptcha/api/siteverify";
        url = QueryHelpers.AddQueryString(url, "secret", Program.Config!["google_recaptcha_secret_key"]);
        url = QueryHelpers.AddQueryString(url, "response", token);
        HttpResponseMessage response;
        try {
            response = await client.PostAsync(url, null);
        }
        catch (Exception e) {
            Logger.Error("Error validating recaptcha: " + e);
            return new GoogleReCaptchaResponse(false);
        }
        
        string json = await response.Content.ReadAsStringAsync();
        GoogleReCaptchaResponse result = JsonConvert.DeserializeObject<GoogleReCaptchaResponse>(json)!;
        Logger.Debug($"ReCaptcha Score: {result.Score}");
        return result;
    }
    
}