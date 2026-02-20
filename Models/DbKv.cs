using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

public class DbKv {
    [Key]
    [StringLength(64)]
    public string Key { get; set; } = null!;
    
    [StringLength(2048)]
    public string Value { get; set; } = null!;
}
