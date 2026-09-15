using System.Text.Json.Serialization;

namespace SerbleAPI.Data.Schemas;

public sealed record PasskeyDetails(bool IsBackupEligible, bool IsBackedUp);

public sealed record CredentialView(
    string Id,
    string Type,
    string? Name,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyDetails? Passkey,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasswordScheme);

public sealed record LoginFlowView(string Id, string[] Methods);

public sealed class CredentialOverview {
    public bool Success { get; init; } = true;
    public CredentialView[] Credentials { get; init; } = [];
    public LoginFlowView[] Flows { get; init; } = [];

    /// <summary>Set when the request ended other sessions; clients should store it over the token they sent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplacementToken { get; set; }

    /// <summary>A replacement for the reauth token the request used, with the same expiry.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplacementReauthToken { get; set; }
}
