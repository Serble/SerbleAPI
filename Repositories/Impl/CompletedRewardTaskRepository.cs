using Microsoft.EntityFrameworkCore;
using SerbleAPI.Models;

namespace SerbleAPI.Repositories.Impl;

public class CompletedRewardTaskRepository(SerbleDbContext db) : ICompletedRewardTaskRepository {

    public Task<bool> HasCompleted(string userId, string taskKey) =>
        db.CompletedRewardTasks.AnyAsync(t => t.UserId == userId && t.TaskKey == taskKey);

    public async Task<bool> AddCompletion(string userId, string taskKey) {
        if (await HasCompleted(userId, taskKey)) return false;
        db.CompletedRewardTasks.Add(new DbCompletedRewardTask {
            Id            = Guid.NewGuid().ToString(),
            UserId        = userId,
            TaskKey       = taskKey,
            DateCompleted = DateTime.UtcNow
        });
        try {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) {
            // Lost a race against a concurrent completion (unique index violation): treat the
            // task as already completed so the reward isn't granted twice.
            return false;
        }
        return true;
    }
}
