using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>A set of methods that together sign the user in.</summary>
[Index(nameof(UserId), nameof(MethodMask), IsUnique = true)]
public class DbUserLoginFlow {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(UserNavigation))]
    public string UserId { get; set; } = null!;

    public int MethodMask { get; set; }

    public DateTime CreatedAt { get; set; }

    public DbUser UserNavigation { get; set; } = null!;
}
