using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbUserAuthorizedApp {
    [Key]
    public int Id { get; set; }
    
    [ForeignKey(nameof(UserNavigation))]
    [StringLength(64)]
    public string UserId { get; set; } = null!;
    
    [ForeignKey(nameof(AppNavigation))]
    [StringLength(64)]
    public string AppId { get; set; } = null!;
    
    [StringLength(128)]
    public string Scopes { get; set; } = null!;
    
    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
    public DbApp AppNavigation { get; set; } = null!;
}
