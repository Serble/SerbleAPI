using System.Text;
using OtpNet;
using QRCoder;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Auth;

public static class TotpCodes {
    private static readonly VerificationWindow Window = new(1, 1);

    public static byte[] KeyBytes(UserCredential credential) =>
        (TotpScheme)credential.Scheme == TotpScheme.Base32Secret
            ? Base32Encoding.ToBytes(credential.Secret!)
            : Encoding.UTF8.GetBytes(credential.Secret!);

    /// <summary>The time step <paramref name="code"/> matches for the credential, or null.</summary>
    public static long? MatchStep(UserCredential credential, string? code) {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(credential.Secret)) return null;
        Totp totp = new(KeyBytes(credential));
        return totp.VerifyTotp(code.Trim(), out long step, Window) ? step : null;
    }

    /// <summary>
    /// Finds an active credential the code is valid for and spends that step. Returns the credential,
    /// or null if none matched or the step was already used.
    /// </summary>
    public static async Task<UserCredential?> VerifyAndConsume(ICredentialRepository credentials, string userId, string? code) {
        foreach (UserCredential credential in await credentials.GetActiveCredentials(userId, CredentialType.Totp)) {
            if (MatchStep(credential, code) is not { } step) continue;
            if (await credentials.TryConsumeTotpCounter(credential.Id, step)) return credential;
        }
        return null;
    }

    public static string NewBase32Secret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    public static string OtpAuthUri(byte[] key, string username) =>
        new OtpUri(OtpType.Totp, key, username, "Serble").ToString();

    public static byte[] QrPng(string uri) {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(8);
    }
}
