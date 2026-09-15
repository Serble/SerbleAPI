using System.Net;
using System.Net.Sockets;
using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Services.Auth;

/// <summary>
/// Rate limits guesses at an account's credentials so a stranger's guesses cannot lock the owner out.
/// Each client gets its own budget: a device that has signed in to the account before, or else an
/// address. Unrecognised clients also share one budget per account, which bounds guessing spread
/// across many addresses; recognised devices never touch it.
/// </summary>
public static class LoginAttempts {

    public static RateLimitDecision Charge(IRateLimitService limiter, ITokenService tokens,
        CredentialType method, User user, LoginClient client) {
        string account = CredentialTypes.ToName(method) + ":" + user.Id;

        if (RecognisedDevice(tokens, user, client.DeviceToken) is { } device) {
            return limiter.Check(RateLimitTiers.Login, RateLimitScope.Ip, account + ":device:" + device);
        }

        RateLimitDecision bySource = limiter.Check(RateLimitTiers.Login, RateLimitScope.Ip,
            account + ":addr:" + AddressKey(client.Address));

        // An address already over its own budget draws nothing from the shared one.
        return bySource.Allowed
            ? limiter.Check(RateLimitTiers.Login, RateLimitScope.Identity, account)
            : bySource;
    }

    /// <summary>The device id of a device token issued to this user, or null.</summary>
    private static string? RecognisedDevice(ITokenService tokens, User user, string? token) {
        if (string.IsNullOrEmpty(token) || token.Length > 2048) return null;
        if (!tokens.ValidateDeviceToken(token, out string? userId, out string? deviceId)
            || userId != user.Id
            || string.IsNullOrEmpty(deviceId)) return null;

        // Not a credential, so a revocation cut-off does not apply.
        return deviceId;
    }

    /// <summary>
    /// An IPv6 address counts as its /64, usually one site's whole allocation, so a host cannot
    /// step through its own addresses for a fresh budget each time.
    /// </summary>
    public static string AddressKey(IPAddress? address) {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
