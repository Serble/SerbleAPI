namespace SerbleAPI.Data;

/// <summary>
/// Stable keys identifying reward tasks a user can complete to earn coins. The key is persisted in
/// the CompletedRewardTasks table and used to look up the configured reward amount in the server
/// config (a per-task <c>economy.task_reward.&lt;key&gt;</c> setting). Add a new entry to
/// <see cref="All"/> for each future task and it appears automatically in the admin settings.
/// </summary>
public static class RewardTasks {
    public const string VerifyEmail = "verify_email";

    /// <summary>
    /// Every known reward task: its stable key, a human label for the admin UI, and the default
    /// reward (a decimal coin amount) used until an admin overrides it.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label, string Default)> All = new[] {
        (VerifyEmail, "Verify email", "100"),
    };
}
