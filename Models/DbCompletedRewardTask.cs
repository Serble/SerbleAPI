using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// Records that a user has completed a reward task (and therefore received its coin reward).
/// The unique (UserId, TaskKey) index guarantees a task is only rewarded once per user.
/// </summary>
[Index(nameof(UserId), nameof(TaskKey), IsUnique = true)]
public class DbCompletedRewardTask {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    public string UserId { get; set; } = null!;

    [StringLength(64)]
    public string TaskKey { get; set; } = null!;

    public DateTime DateCompleted { get; set; }
}
