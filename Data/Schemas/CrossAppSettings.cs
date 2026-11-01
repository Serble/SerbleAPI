namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Which other apps may sign a user in to this app server to server (cross-app sign-in). Set by the
/// app's owner: the target app decides who it accepts, not Serble.
/// </summary>
public enum CrossAppPolicy {
    /// <summary>No app may sign users in to this one. The default.</summary>
    Disabled = 0,
    /// <summary>Only official callers. Approved without asking when this app is official too, and
    /// with the user's consent when it is not.</summary>
    OfficialOnly = 1,
    /// <summary>Any confidential app. Official→official pairs are approved without asking; every
    /// other pair needs the user's consent.</summary>
    AnyWithConsent = 2
}

/// <summary>A user's consent for one app (the caller) to sign them in to another (the target).</summary>
public class CrossAppGrant {
    public string CallerAppId { get; set; } = "";
    public string TargetAppId { get; set; } = "";
    public DateTime DateCreated { get; set; }
}
