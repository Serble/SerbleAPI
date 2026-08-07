using System.Net;
using System.Net.Sockets;

namespace SerbleAPI.Data;

/// <summary>
/// Validation for app-supplied webhook endpoints.
/// <para>
/// A webhook URL is an arbitrary address the server will make outbound requests to on a schedule,
/// which makes it a server-side request forgery primitive if left unchecked: an app could point one
/// at the database host, a cloud metadata endpoint, or anything else reachable from inside the
/// network and read the response codes back through its own delivery log. So registration requires
/// HTTPS and a publicly routable host by default, both overridable by an admin for deployments
/// where apps genuinely live on the same private network.
/// </para>
/// <para>
/// This is a registration-time check, not a guarantee — a hostname can be re-pointed at a private
/// address after the fact. It raises the bar rather than closing the hole; a deployment that treats
/// egress as untrusted should also restrict it at the network layer.
/// </para>
/// </summary>
public static class WebhookUrl {
    public const int MaxLength = 512;

    /// <summary>
    /// Checks shape only — scheme, length, no credentials, no fragment. Never touches the network,
    /// so it is safe to call anywhere.
    /// </summary>
    public static bool TryValidateFormat(string? raw, bool allowInsecure, out string normalised, out string? error) {
        normalised = (raw ?? "").Trim();
        error = null;

        if (normalised.Length == 0) {
            error = "A webhook URL is required.";
            return false;
        }
        if (normalised.Length > MaxLength) {
            error = $"A webhook URL cannot be longer than {MaxLength} characters.";
            return false;
        }
        if (!Uri.TryCreate(normalised, UriKind.Absolute, out Uri? uri)) {
            error = "The webhook URL must be an absolute URL.";
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp)) {
            error = allowInsecure
                ? "The webhook URL must use http or https."
                : "The webhook URL must use https.";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            error = "The webhook URL must not contain credentials.";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.Fragment)) {
            error = "The webhook URL must not contain a fragment.";
            return false;
        }

        normalised = uri.ToString();
        return normalised.Length <= MaxLength;
    }

    /// <summary>
    /// Rejects hosts that resolve into address space the public internet cannot reach. A DNS
    /// failure is not treated as a rejection: the name may simply not be resolvable from here yet,
    /// and refusing to register in that case would break legitimate endpoints for a reason the app
    /// owner cannot act on.
    /// </summary>
    public static async Task<string?> CheckHostIsPublic(string url, CancellationToken cancellationToken = default) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return "The webhook URL must be an absolute URL.";

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? literal)) {
            return IsPrivate(literal)
                ? "The webhook URL must not point at a private or loopback address."
                : null;
        }

        IPAddress[] resolved;
        try {
            resolved = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException) {
            return null;
        }
        catch (ArgumentException) {
            return "The webhook URL host is not a valid hostname.";
        }

        return resolved.Any(IsPrivate)
            ? "The webhook URL host resolves to a private or loopback address."
            : null;
    }

    private static bool IsPrivate(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork) {
            byte[] b = address.GetAddressBytes();
            return b[0] switch {
                0 => true,                                   // "this network"
                10 => true,                                  // 10.0.0.0/8
                127 => true,                                 // loopback
                169 when b[1] == 254 => true,                // 169.254.0.0/16 link-local (cloud metadata)
                172 when b[1] >= 16 && b[1] <= 31 => true,   // 172.16.0.0/12
                192 when b[1] == 168 => true,                // 192.168.0.0/16
                100 when b[1] >= 64 && b[1] <= 127 => true,  // 100.64.0.0/10 carrier-grade NAT
                >= 224 => true,                              // multicast + reserved
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6) {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
            byte[] b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;                       // fc00::/7 unique local
            if (b.All(x => x == 0)) return true;                          // ::
        }

        return false;
    }
}
