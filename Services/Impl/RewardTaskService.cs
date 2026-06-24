using Microsoft.Extensions.Options;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.Services.Impl;

public class RewardTaskService(
    ILogger<RewardTaskService> logger,
    IOptions<EconomySettings> economy,
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

        ulong wholeCoins = economy.Value.TaskRewards.GetValueOrDefault(taskKey, 0UL);
        if (wholeCoins == 0) {
            // Task tracked as complete, but no reward is configured for it.
            return true;
        }

        // TaskRewards is authored in whole coins; balances store raw fixed-point units.
        ulong amount = CoinFixedPoint.FromWholeCoins(wholeCoins);
        await balances.AddCoins(BalanceOwnerType.User, userId, amount, $"Reward: {taskKey}");
        logger.LogInformation("Granted {Coins} coins ({Raw} raw) to user {UserId} for reward task {TaskKey}",
            wholeCoins, amount, userId, taskKey);
        return true;
    }
}
