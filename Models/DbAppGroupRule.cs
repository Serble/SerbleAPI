using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

/// <summary>
/// Per-app group gate. A row marks a group as part of an app's allow-list
/// (<see cref="Deny"/> = false) or deny-list (<see cref="Deny"/> = true).
/// </summary>
public class DbAppGroupRule {
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(AppNavigation))]
    [StringLength(64)]
    public string AppId { get; set; } = null!;

    [ForeignKey(nameof(GroupNavigation))]
    [StringLength(64)]
    public string GroupId { get; set; } = null!;

    public bool Deny { get; set; }

    // navigation properties
    public DbApp AppNavigation { get; set; } = null!;
    public DbGroup GroupNavigation { get; set; } = null!;
}
