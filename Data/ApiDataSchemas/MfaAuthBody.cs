using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class MfaAuthBody {
    [JsonPropertyName("login_token")]
    public string? LoginToken { get; set; }
    
    [JsonPropertyName("totp_code")]
    [Required]
    public string TotpCode { get; set; } = null!;
}