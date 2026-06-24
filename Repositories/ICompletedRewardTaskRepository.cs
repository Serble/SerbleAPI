namespace SerbleAPI.Repositories;

public interface ICompletedRewardTaskRepository {
    /// <summary>Whether the user has already completed (and been rewarded for) the task.</summary>
    Task<bool> HasCompleted(string userId, string taskKey);

    /// <summary>
    /// Records that the user completed the task. Returns false if a completion already existed
    /// (so the caller can avoid granting the reward twice under concurrent calls).
    /// </summary>
    Task<bool> AddCompletion(string userId, string taskKey);
}
