using System.Text.Json.Serialization;

namespace SerbleAPI.Data.ApiDataSchemas;

/// <summary>
/// RFC 6749 / OIDC token endpoint response. <see cref="IdToken"/> and
/// <see cref="RefreshToken"/> are only populated when the corresponding scopes
/// (openid / offline_access) were granted.
/// </summary>
public class OidcTokenResponse {

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("id_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdToken { get; set; }

    [JsonPropertyName("refresh_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";
}
