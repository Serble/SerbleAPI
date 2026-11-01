using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

/// <summary>
/// A user's consent for one app (the caller) to sign them in to another (the target) through
/// cross-app sign-in. Official→official pairs never need one; every other pair does. Deleted with
/// the user or with either app.
/// </summary>
public class DbUserCrossAppGrant {
    [Key]
    public int Id { get; set; }

    [StringLength(64)]
    public string UserId { get; set; } = null!;

    [StringLength(64)]
    public string CallerAppId { get; set; } = null!;

    [StringLength(64)]
    public string TargetAppId { get; set; } = null!;

    public DateTime DateCreated { get; set; }

    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
    public DbApp CallerAppNavigation { get; set; } = null!;
    public DbApp TargetAppNavigation { get; set; } = null!;
}
