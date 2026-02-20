using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbOwnedProduct {
    [Key]
    public int Id { get; set; }
    
    [StringLength(64)]
    [ForeignKey(nameof(UserNavigation))]
    public string User { get; set; } = null!;
    
    [StringLength(64)]
    public string Product { get; set; } = null!;
    
    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
}
