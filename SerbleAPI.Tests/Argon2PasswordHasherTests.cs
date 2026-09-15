using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Services;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Tests;

public class Argon2PasswordHasherTests {
    private static Argon2PasswordHasher Hasher(int memoryKiB = 8192, int iterations = 3) =>
        new(Options.Create(new PasswordHashingSettings { MemoryKiB = memoryKiB, Iterations = iterations, MaxConcurrency = 2 }));

    private static UserCredential Password(string secret, PasswordScheme scheme, string? salt = null) => new() {
        Type = CredentialType.Password,
        Status = CredentialStatus.Active,
        Secret = secret,
        Scheme = (int)scheme,
        LegacySalt = salt
    };

    [Theory]
    [InlineData("hunter2", "saltsaltsalt")]
    [InlineData("hunter2", "")]
    [InlineData("pa:ss wörd€", "abc")]
    public async Task LegacySha256_VerifiesAndNeedsUpgrade(string password, string salt) {
        using Argon2PasswordHasher hasher = Hasher();
        string legacy = (password + salt).Sha256Hash();

        PasswordCheck ok = await hasher.Verify(Password(legacy, PasswordScheme.LegacySha256, salt), password);
        Assert.True(ok.Valid);
        Assert.True(ok.NeedsUpgrade);
        Assert.Equal(legacy, ok.LegacyDigest);

        Assert.False((await hasher.Verify(Password(legacy, PasswordScheme.LegacySha256, salt), password + "x")).Valid);
    }

    [Fact]
    public async Task WrappedLegacy_Verifies() {
        using Argon2PasswordHasher hasher = Hasher();
        const string salt = "0123456789";
        string legacy = ("correct horse" + salt).Sha256Hash();
        string wrapped = await hasher.Hash(legacy);

        Assert.True(await hasher.VerifyHash(wrapped, legacy));
        PasswordCheck ok = await hasher.Verify(Password(wrapped, PasswordScheme.Argon2idOverSha256, salt), "correct horse");
        Assert.True(ok.Valid);
        Assert.True(ok.NeedsUpgrade);
        Assert.False((await hasher.Verify(Password(wrapped, PasswordScheme.Argon2idOverSha256, salt), "wrong")).Valid);
    }

    [Fact]
    public async Task Argon2id_VerifiesExactString() {
        using Argon2PasswordHasher hasher = Hasher();
        string hash = await hasher.Hash("pässwörd");

        Assert.StartsWith("$argon2id$", hash);
        PasswordCheck ok = await hasher.Verify(Password(hash, PasswordScheme.Argon2id), "pässwörd");
        Assert.True(ok.Valid);
        Assert.False(ok.NeedsUpgrade);
        // No normalisation: a different encoding of the same text is a different password.
        Assert.False((await hasher.Verify(Password(hash, PasswordScheme.Argon2id), "pässwörd")).Valid);
    }

    [Fact]
    public async Task Argon2id_NeedsUpgradeWhenParametersChange() {
        string hash = await Hasher(iterations: 3).Hash("hunter2");
        PasswordCheck check = await Hasher(iterations: 4).Verify(Password(hash, PasswordScheme.Argon2id), "hunter2");
        Assert.True(check.Valid);
        Assert.True(check.NeedsUpgrade);
    }
}
