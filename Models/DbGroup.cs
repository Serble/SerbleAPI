using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

public class DbGroup {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    public string Name { get; set; } = null!;

    [StringLength(512)]
    public string Description { get; set; } = null!;
}
