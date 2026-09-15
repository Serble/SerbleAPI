namespace SerbleAPI.Data.Schemas;

/// <summary>A kind of sign-in method. Values are persisted, and a flow stores them as bits (<c>1 &lt;&lt; value</c>).</summary>
public enum CredentialType {
    Password = 1,
    Totp = 2,
    Passkey = 3
    // 4 and 5 are reserved for email and SMS codes.
}

public enum CredentialStatus {
    Pending = 0,
    Active = 1
}

public enum PasswordScheme {
    /// <summary>Hex <c>SHA256(password + salt)</c>, held only until the upgrade job wraps it.</summary>
    LegacySha256 = 1,
    /// <summary>Argon2id over the legacy hex digest.</summary>
    Argon2idOverSha256 = 2,
    Argon2id = 3
}

public enum TotpScheme {
    /// <summary>The key is the UTF-8 bytes of the stored string.</summary>
    LegacyAsciiSecret = 1,
    Base32Secret = 2
}

public static class CredentialTypes {
    public static readonly CredentialType[] All = [CredentialType.Password, CredentialType.Totp, CredentialType.Passkey];

    public static readonly int KnownMask = ToMask(All);

    /// <summary>Methods whose secret is a short guessable code.</summary>
    public static readonly int OneTimeCodeMask = Bit(CredentialType.Totp);

    public static int Bit(CredentialType type) => 1 << (int)type;

    public static int ToMask(IEnumerable<CredentialType> types) => types.Aggregate(0, (mask, t) => mask | Bit(t));

    public static CredentialType[] FromMask(int mask) => All.Where(t => (mask & Bit(t)) != 0).ToArray();

    public static string ToName(CredentialType type) => type switch {
        CredentialType.Password => "password",
        CredentialType.Totp     => "totp",
        CredentialType.Passkey  => "passkey",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static bool TryParse(string? name, out CredentialType type) {
        foreach (CredentialType t in All) {
            if (!string.Equals(ToName(t), name, StringComparison.OrdinalIgnoreCase)) continue;
            type = t;
            return true;
        }
        type = default;
        return false;
    }

    public static string[] Names(int mask) => FromMask(mask).Select(ToName).ToArray();
}
