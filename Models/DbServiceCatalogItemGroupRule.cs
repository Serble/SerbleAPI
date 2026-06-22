using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbServiceCatalogItemGroupRule {
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(ServiceNavigation))]
    [StringLength(64)]
    public string ServiceId { get; set; } = null!;

    [ForeignKey(nameof(GroupNavigation))]
    [StringLength(64)]
    public string GroupId { get; set; } = null!;

    public DbServiceCatalogItem ServiceNavigation { get; set; } = null!;
    public DbGroup GroupNavigation { get; set; } = null!;
}
