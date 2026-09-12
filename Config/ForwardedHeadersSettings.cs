namespace SerbleAPI.Config;

/// <summary>
/// How the API recovers the real client address when it sits behind a reverse proxy. The defaults
/// suit nginx and Traefik alike, since both send <c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>
/// and <c>X-Forwarded-Host</c>.
/// <para>
/// Rate limiting partitions anonymous traffic by client address, so getting this wrong is not a
/// stricter limit but one bucket for the entire internet: the first attacker to trip it locks out
/// everyone behind the same proxy.
/// </para>
/// </summary>
public class ForwardedHeadersSettings {

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Proxy addresses to trust, e.g. <c>127.0.0.1</c>. An entry here is permission to rewrite the
    /// client's address, so the list is exact.
    /// <para>
    /// Empty (with <see cref="KnownNetworks"/> also empty) falls back to loopback. That fallback
    /// lives in the startup code rather than in a default value here because the configuration
    /// binder <i>appends</i> to a non-empty array default instead of replacing it, so a default of
    /// <c>["127.0.0.1"]</c> would keep trusting loopback even after an operator replaced the list.
    /// </para>
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy subnets in CIDR form, e.g. <c>172.16.0.0/12</c> for a Docker bridge.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// How many entries to walk back from the right of the forwarded chain. One proxy means 1;
    /// every extra hop is one more address a client can forge.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Accept forwarded headers from any peer, for container networks where the proxy's address
    /// moves. Only safe while <see cref="ApiSettings.BindUrl"/> is unreachable except through the
    /// proxy; if that port is exposed, clients can forge their own source address.
    /// </summary>
    public bool TrustAllProxies { get; set; }

    /// <summary>Set to <c>X-Real-IP</c> for an nginx config that only sets that one.</summary>
    public string ForwardedForHeaderName { get; set; } = "X-Forwarded-For";

    public string ForwardedProtoHeaderName { get; set; } = "X-Forwarded-Proto";

    public string ForwardedHostHeaderName { get; set; } = "X-Forwarded-Host";

    /// <summary>
    /// Whether every forwarded header must be present and the same length. Off, because common
    /// nginx recipes send some and not others.
    /// </summary>
    public bool RequireHeaderSymmetry { get; set; }

    /// <summary>Hostnames the proxy may claim in <c>X-Forwarded-Host</c>. Empty accepts as sent.</summary>
    public string[] AllowedHosts { get; set; } = [];
}
