namespace SerbleAPI.Services;

public interface IRewardTaskService {
    /// <summary>
    /// Grants the configured coin reward for a task the user has just completed, exactly once
    /// per user. Returns true if the reward was granted by this call, false if the user had
    /// already completed the task (or no reward is configured for it).
    /// </summary>
    Task<bool> TryGrantReward(string userId, string taskKey);
}
