using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services;

/// <param name="NeedsUpgrade">The password is right but stored under an old scheme or old parameters.</param>
/// <param name="LegacyDigest">The legacy SHA-256 hex the password produced, for legacy schemes.</param>
public readonly record struct PasswordCheck(bool Valid, bool NeedsUpgrade, string? LegacyDigest);

public interface IPasswordHasher {
    Task<string> Hash(string input, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="input"/> matches an encoded Argon2id hash.</summary>
    Task<bool> VerifyHash(string encodedHash, string input, CancellationToken cancellationToken = default);

    Task<PasswordCheck> Verify(UserCredential credential, string password, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when no hashing slot frees up in time. Answered with 503.</summary>
public class PasswordHasherBusyException() : Exception("Password hashing is at capacity");
