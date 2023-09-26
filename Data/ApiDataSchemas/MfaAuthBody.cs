using System.Text.Json.Serialization;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class MfaAuthBody {
    [JsonPropertyName("login_token")]
    public string LoginToken { get; set; } = null!;
    
    [JsonPropertyName("totp_code")]
    public string TotpCode { get; set; } = null!;
}