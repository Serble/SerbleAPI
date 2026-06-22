using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

public class DbServiceCatalogItem {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(1024)]
    public string Description { get; set; } = null!;

    public string Url { get; set; } = null!;

    public string? IconUrl { get; set; }

    public int VisibilityMode { get; set; }
}
