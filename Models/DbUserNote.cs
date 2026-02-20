using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbUserNote {
    [StringLength(64)]
    [ForeignKey(nameof(UserNavigation))]
    public string User { get; set; } = null!;
    
    [Key]
    [StringLength(64)]
    public string NoteId { get; set; } = null!;
    
    public string Note { get; set; } = null!;
    
    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
}
