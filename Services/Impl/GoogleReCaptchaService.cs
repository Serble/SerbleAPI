using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Impl; 

public class GoogleReCaptchaService(IOptions<ReCaptchaSettings> settings) : IGoogleReCaptchaService {
    
    public async Task<GoogleReCaptchaResponse> VerifyReCaptcha(string token) {
        HttpClient client = new();
        string url = "https://www.google.com/recaptcha/api/siteverify";
        url = QueryHelpers.AddQueryString(url, "secret", settings.Value.SecretKey);
        url = QueryHelpers.AddQueryString(url, "response", token);
        HttpResponseMessage response;
        try {
            response = await client.PostAsync(url, null);
        }
        catch (Exception) {
            return new GoogleReCaptchaResponse(false);
        }
        
        string json = await response.Content.ReadAsStringAsync();
        GoogleReCaptchaResponse result = JsonConvert.DeserializeObject<GoogleReCaptchaResponse>(json)!;
        return result;
    }
}