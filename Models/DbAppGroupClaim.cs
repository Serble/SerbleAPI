using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

/// <summary>
/// Per-app group claim mapping. When an authenticated user belongs to <see cref="GroupId"/>,
/// the app receives <see cref="ClaimValue"/> in its <c>groups</c> claim. This lets each app
/// see only the group values relevant to it, never Serble's internal group structure.
/// </summary>
public class DbAppGroupClaim {
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(AppNavigation))]
    [StringLength(64)]
    public string AppId { get; set; } = null!;

    [ForeignKey(nameof(GroupNavigation))]
    [StringLength(64)]
    public string GroupId { get; set; } = null!;

    [StringLength(128)]
    public string ClaimValue { get; set; } = null!;

    // navigation properties
    public DbApp AppNavigation { get; set; } = null!;
    public DbGroup GroupNavigation { get; set; } = null!;
}
