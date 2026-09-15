using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using Sodium;

namespace SerbleAPI.Services.Impl;

public sealed class Argon2PasswordHasher : IPasswordHasher, IDisposable {
    private readonly SemaphoreSlim _slots;
    private readonly long _iterations;
    private readonly int _memoryBytes;
    private readonly TimeSpan _queueTimeout;

    public Argon2PasswordHasher(IOptions<PasswordHashingSettings> options) {
        PasswordHashingSettings settings = options.Value;
        // Sodium.Core rejects fewer than 3 passes.
        _iterations = Math.Max(3, settings.Iterations);
        _memoryBytes = (int)Math.Clamp(settings.MemoryKiB * 1024L, 8 * 1024, int.MaxValue);
        _queueTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.QueueTimeoutSeconds));
        int slots = Math.Max(1, settings.MaxConcurrency);
        _slots = new SemaphoreSlim(slots, slots);
    }

    public Task<string> Hash(string input, CancellationToken cancellationToken = default) =>
        InSlot(() => PasswordHash.ArgonHashString(input, _iterations, _memoryBytes), cancellationToken);

    public Task<bool> VerifyHash(string encodedHash, string input, CancellationToken cancellationToken = default) =>
        InSlot(() => PasswordHash.ArgonHashStringVerify(encodedHash, input), cancellationToken);

    public async Task<PasswordCheck> Verify(UserCredential credential, string password,
        CancellationToken cancellationToken = default) {
        if (credential.Type != CredentialType.Password || string.IsNullOrEmpty(credential.Secret)) return default;

        switch ((PasswordScheme)credential.Scheme) {
            case PasswordScheme.LegacySha256: {
                string digest = (password + (credential.LegacySalt ?? "")).Sha256Hash();
                bool valid = OidcCrypto.FixedTimeEquals(digest, credential.Secret);
                return new PasswordCheck(valid, valid, digest);
            }
            case PasswordScheme.Argon2idOverSha256: {
                string digest = (password + (credential.LegacySalt ?? "")).Sha256Hash();
                bool valid = await VerifyHash(credential.Secret, digest, cancellationToken);
                return new PasswordCheck(valid, valid, digest);
            }
            case PasswordScheme.Argon2id: {
                bool valid = await VerifyHash(credential.Secret, password, cancellationToken);
                bool stale = valid && PasswordHash.ArgonPasswordNeedsRehash(credential.Secret, _iterations, _memoryBytes);
                return new PasswordCheck(valid, stale, null);
            }
            default:
                return default;
        }
    }

    private async Task<T> InSlot<T>(Func<T> work, CancellationToken cancellationToken) {
        if (!await _slots.WaitAsync(_queueTimeout, cancellationToken)) throw new PasswordHasherBusyException();
        try {
            return work();
        }
        finally {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
