namespace SerbleAPI.Data.Schemas;

/// <summary>
/// The login gate Serble applies before issuing an authorization code for an app. This is
/// admin-owned metadata and is never exposed through owner-facing or public app responses.
/// </summary>
public enum AppAccessPolicy {
    /// <summary>Any logged-in, enabled user may sign in.</summary>
    AllowAllUsers = 0,
    /// <summary>Only users with a verified email may sign in.</summary>
    RequireVerifiedEmail = 1,
    /// <summary>Only users in at least one of the app's allowed groups may sign in.</summary>
    RequireGroups = 2,
    /// <summary>Only users at or above a minimum PermLevel may sign in.</summary>
    RequireMinimumPermLevel = 3,
    /// <summary>No one may sign in (app disabled for SSO).</summary>
    Disabled = 4
}
