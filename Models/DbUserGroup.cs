using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbUserGroup {
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(UserNavigation))]
    [StringLength(64)]
    public string UserId { get; set; } = null!;

    [ForeignKey(nameof(GroupNavigation))]
    [StringLength(64)]
    public string GroupId { get; set; } = null!;

    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
    public DbGroup GroupNavigation { get; set; } = null!;
}
