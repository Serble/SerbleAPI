using System.Security.Cryptography;
using System.Text;

namespace SerbleAPI.Data;

/// <summary>
/// Derives a stable, human-readable four-word phrase from an opaque identifier (an app id, item id,
/// …). The phrase is a fingerprint, not a reversible encoding: its purpose is to let a person
/// eyeball-compare, read out, or recognise an id without transcribing a long random handle.
/// <para>
/// Backed by a fixed embedded 2048-word list (<c>Data/Raw/encode-words.txt</c>), so each word carries 11 bits
/// and a four-word phrase carries 44 bits. Two distinct ids collide on the same phrase only about
/// 1 in 2^44 (~1.8e13) of the time, which is comfortably rare for visual identification. The mapping
/// is purely a function of the id, so the same id always yields the same phrase across requests and
/// across the apps/items that reference it.
/// </para>
/// </summary>
public static class WordId {
    private const int WordCount = 2048; // 11 bits per word
    private const string ResourceName = "SerbleAPI.Data.Raw.encode-words.txt";
    private static string[] _words = null!;

    /// <summary>Loads the word list. Call once at startup (mirrors <see cref="Raw.RawDataManager"/>).</summary>
    public static void Load() {
        using Stream? stream = typeof(WordId).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");

        using StreamReader reader = new(stream);
        _words = reader.ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .ToArray();
        if (_words.Length < WordCount)
            throw new InvalidOperationException(
                $"encode-words.txt must contain at least {WordCount} words, found {_words.Length}.");
    }

    /// <summary>
    /// The four-word phrase for <paramref name="id"/> (e.g. <c>"ladder-puzzle-orbit-canyon"</c>), or
    /// an empty string when the id is null/empty.
    /// </summary>
    public static string Encode(string? id) {
        if (string.IsNullOrEmpty(id)) return "";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));

        // Pull the leading 44 bits (4 × 11) out of the digest, six bytes at a time.
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits = (bits << 8) | hash[i];

        string[] picked = new string[4];
        for (int i = 3; i >= 0; i--) {
            picked[i] = _words[(int)(bits & 0x7FF) % WordCount];
            bits >>= 11;
        }
        return string.Join('-', picked);
    }
}
