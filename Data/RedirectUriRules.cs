namespace SerbleAPI.Data;

/// <summary>
/// What an app owner may store as a redirect target, shared by the create and edit routes so the
/// two cannot disagree about what is acceptable.
///
/// <para><b>What this blocks, and why only that.</b> A redirect URI is a place this server sends a
/// browser carrying an authorization code. The danger is a target that makes the browser
/// <i>execute</i> something rather than go somewhere: a <c>javascript:</c> or <c>data:</c> value
/// turns the authorize endpoint into a script-execution sink on any page that follows it, and
/// <c>file:</c> aims it at the local disk. Those schemes are refused by name (see
/// <see cref="BlockedSchemes"/>) and everything else is allowed.</para>
///
/// <para><b>Why not an https allow-list.</b> Native apps do not have an https callback. RFC 8252
/// gives them two options and both have to keep working: a loopback server on an arbitrary port
/// (<c>http://127.0.0.1:49152/cb</c>) and a private-use scheme (<c>com.example.app:/oauth</c>,
/// <c>myapp://callback</c>). Plain http to a named host is allowed for the same reason — a LAN
/// hostname during development is a normal registration, not an attack.</para>
///
/// <para><b>Validation happens on write only.</b> Values already stored are never re-checked, so
/// tightening these rules cannot break an app that is already working;
/// <see cref="Schemas.OAuthApp.IsValidRedirectUri"/> matches registered values exactly and never
/// follows one that was not registered.</para>
/// </summary>
public static class RedirectUriRules {
    /// <summary>
    /// Schemes an app may never register, because following one executes script or reads a local
    /// resource instead of navigating. Compared case-insensitively against
    /// <see cref="Uri.Scheme"/>, which is already lower-cased by <see cref="Uri"/>.
    /// <para>
    /// A deny-list rather than an allow-list: the set of schemes that are dangerous here is small
    /// and well known, while the set that is legitimate includes every private-use scheme any
    /// native app might pick.
    /// </para>
    /// </summary>
    public static readonly string[] BlockedSchemes = [
        "javascript", "data", "vbscript", "blob", "about", "file", "filesystem", "view-source"
    ];

    /// <summary>
    /// The whole <c>;</c>-separated field. Generous, but bounded: the column is a longtext, and
    /// every entry in it is a value the authorize endpoint has to scan on each request.
    /// </summary>
    public const int MaxTotalLength = 2048;

    /// <summary>Cap on one entry, so a single pathological value cannot fill the field.</summary>
    public const int MaxUriLength = 512;

    /// <summary>Cap on the number of entries, matching the total-length budget.</summary>
    public const int MaxUriCount = 16;

    /// <summary>
    /// Validates the stored <c>;</c>-separated redirect-URI field. An empty field is accepted: an
    /// app that has not registered a redirect target simply cannot complete an authorization flow.
    /// <paramref name="error"/> is null when the field is storable and otherwise carries a message
    /// meant for the caller.
    /// </summary>
    public static bool TryValidateField(string? raw, out string? error) {
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (raw.Length > MaxTotalLength) {
            error = $"Redirect URIs cannot be longer than {MaxTotalLength} characters in total";
            return false;
        }

        // Split on the same separator AllRedirectUris reads the field with, so exactly the values
        // that will later be treated as registered targets are the values checked here.
        string[] entries = raw.Split(';');
        int checkedCount = 0;
        foreach (string entry in entries) {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            checkedCount++;
            if (checkedCount > MaxUriCount) {
                error = $"Cannot register more than {MaxUriCount} redirect URIs";
                return false;
            }
            if (!TryValidateOne(entry, out error)) return false;
        }
        return true;
    }

    /// <summary>
    /// Validates one redirect URI. <paramref name="error"/> is null when it is acceptable and
    /// otherwise carries a message meant for the caller.
    /// </summary>
    public static bool TryValidateOne(string? raw, out string? error) {
        error = null;

        if (string.IsNullOrWhiteSpace(raw)) {
            error = "A redirect URI cannot be empty";
            return false;
        }

        // Leading or trailing whitespace would be stored verbatim and then never match the value
        // a client actually sends, so reject it rather than silently trimming.
        if (raw != raw.Trim()) {
            error = "A redirect URI cannot start or end with whitespace";
            return false;
        }

        if (raw.Length > MaxUriLength) {
            error = $"A redirect URI cannot be longer than {MaxUriLength} characters";
            return false;
        }

        // A CR or LF in the value splits the Location header when the authorize endpoint redirects,
        // and the rest are simply not valid in a URI.
        foreach (char c in raw) {
            if (char.IsControl(c)) {
                error = "A redirect URI cannot contain control characters";
                return false;
            }
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)) {
            error = "A redirect URI must be an absolute URI (for example https://example.com/callback)";
            return false;
        }

        // Note this also catches a scheme-relative value like "//evil.example/cb", which Uri
        // resolves to the file scheme rather than to a network address.
        if (BlockedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) {
            error = $"A redirect URI cannot use the {uri.Scheme} scheme";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            error = "A redirect URI cannot contain credentials";
            return false;
        }

        // The fragment never reaches the server, so a registered one cannot be matched against
        // what a client sends and only ever creates a mismatch the owner cannot see.
        if (!string.IsNullOrEmpty(uri.Fragment)) {
            error = "A redirect URI cannot contain a fragment";
            return false;
        }

        // Only web URLs need a host. A private-use scheme legitimately has none at all
        // ("com.example.app:/oauth2redirect"), so requiring one would rule out exactly the native
        // apps this is meant to keep working.
        bool isWeb = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        if (isWeb && string.IsNullOrEmpty(uri.Host)) {
            error = "An http or https redirect URI must have a host";
            return false;
        }

        // Deliberately not checked: whether the value matches the form Uri would normalise it to.
        // Registered targets are compared to what a client sends with an exact ordinal match, so an
        // unusual-but-harmless spelling only ever fails to match — it cannot widen what this app
        // accepts — and rejecting it here would turn a working registration into an error.
        return true;
    }
}
