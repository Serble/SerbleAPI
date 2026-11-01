using System.Text.Json.Serialization;

namespace SerbleAPI.Data.ApiDataSchemas; 

public class AccessTokenResponse {
    
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = null!;
    
    [JsonPropertyName("expires_in")]
    public uint ExpiresIn { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = null!;

    /// <summary>
    /// The app that obtained the code being redeemed through cross-app sign-in, so the redeeming
    /// app can tell the session apart from one the user started themselves. Omitted otherwise.
    /// </summary>
    [JsonPropertyName("via_app")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ViaApp { get; set; }
}