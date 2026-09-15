namespace SerbleAPI.Data.Schemas;

public class UserCredential {
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public CredentialType Type { get; set; }
    public string? Name { get; set; }
    public CredentialStatus Status { get; set; }
    public string? Secret { get; set; }
    /// <summary>A <see cref="PasswordScheme"/> or <see cref="TotpScheme"/> value, depending on <see cref="Type"/>.</summary>
    public int Scheme { get; set; }
    public string? LegacySalt { get; set; }
    public long? Counter { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

/// <param name="Mask">The flow's methods, as <see cref="CredentialTypes.Bit"/> bits.</param>
public readonly record struct LoginFlow(string Id, int Mask);
