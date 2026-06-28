using SerbleAPI.Config;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class RewardTaskService(
    ILogger<RewardTaskService> logger,
    IServerConfigService config,
    ICompletedRewardTaskRepository completedTasks,
    IBalanceRepository balances) : IRewardTaskService {

    public async Task<bool> TryGrantReward(string userId, string taskKey) {
        if (await completedTasks.HasCompleted(userId, taskKey)) {
            return false;
        }

        // Claim the completion first: the unique (UserId, TaskKey) index makes this the lock, so
        // concurrent confirmations can never grant the reward twice. Only the winner pays out.
        if (!await completedTasks.AddCompletion(userId, taskKey)) {
            return false;
        }

        // The reward is a per-task coin setting (decimals allowed), stored as a coin amount and
        // read here as raw fixed-point units.
        ulong amount = await config.GetCoinsRaw(ServerConfigCatalog.TaskRewardKey(taskKey));
        if (amount == 0) {
            // Task tracked as complete, but no reward is configured for it.
            return true;
        }

        await balances.AddCoins(BalanceOwnerType.User, userId, amount, $"Reward: {taskKey}");
        logger.LogInformation("Granted {Raw} raw coins to user {UserId} for reward task {TaskKey}",
            amount, userId, taskKey);
        return true;
    }
}
