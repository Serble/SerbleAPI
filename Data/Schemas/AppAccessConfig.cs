namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Admin-only access configuration for an app: the login gate plus the group allow/deny
/// lists and the per-app group→claim mapping. This object is only ever surfaced through
/// admin endpoints; it is deliberately kept separate from <see cref="OAuthApp"/> so it can
/// never leak into owner-facing or anonymous app responses.
/// </summary>
public class AppAccessConfig {
    public string AppId { get; set; }
    public AppAccessPolicy AccessPolicy { get; set; }
    public int? RequiredPermLevel { get; set; }
    public string[] AllowedGroupIds { get; set; }
    public string[] DeniedGroupIds { get; set; }

    /// <summary>Maps an internal group id to the value emitted in this app's <c>groups</c> claim.</summary>
    public Dictionary<string, string> GroupClaimMappings { get; set; }

    public AppAccessConfig() {
        AppId = "";
        AccessPolicy = AppAccessPolicy.AllowAllUsers;
        RequiredPermLevel = null;
        AllowedGroupIds = [];
        DeniedGroupIds = [];
        GroupClaimMappings = new Dictionary<string, string>();
    }
}
