using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbApp {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    [StringLength(64)]
    [ForeignKey(nameof(OwnerNavigation))]
    public string OwnerId { get; set; } = null!;
    
    [StringLength(64)]
    public string Name { get; set; } = null!;
    
    [StringLength(1024)]
    public string Description { get; set; } = null!;
    
    [StringLength(64)]
    public string ClientSecret { get; set; } = null!;
    
    public string RedirectUri { get; set; } = null!;
    
    // navigation properties
    public DbUser OwnerNavigation { get; set; } = null!;
}
