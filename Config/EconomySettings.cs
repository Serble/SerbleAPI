namespace SerbleAPI.Config;

public class EconomySettings {
    /// <summary>
    /// Coin reward granted for completing a reward task, keyed by task key (see
    /// <see cref="SerbleAPI.Data.RewardTasks"/>). Amounts are expressed in <b>whole coins</b> and
    /// are converted to raw fixed-point units when granted. A task with no entry, or an amount of
    /// 0, grants no coins.
    /// </summary>
    public Dictionary<string, ulong> TaskRewards { get; set; } = new();
}
