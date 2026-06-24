namespace SerbleAPI.Data;

/// <summary>
/// Stable keys identifying reward tasks a user can complete to earn coins. The key is
/// persisted in the CompletedRewardTasks table and used to look up the configured reward
/// amount in <see cref="SerbleAPI.Config.EconomySettings.TaskRewards"/>. Add a new constant
/// here for each future task.
/// </summary>
public static class RewardTasks {
    public const string VerifyEmail = "verify_email";
}
