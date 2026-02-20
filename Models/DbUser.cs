using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

public class DbUser {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    [StringLength(255)]
    public string Username { get; set; } = null!;
    
    [StringLength(255)]  // by standard, emails can be up to 254 characters long
    public string? Email { get; set; }
    
    [StringLength(64)]
    public string? Password { get; set; }
    
    public int PermLevel { get; set; }
    
    public bool VerifiedEmail { get; set; }
    
    public int PremiumLevel { get; set; }
    
    /// <summary>
    /// The stripe subscription id
    /// </summary>
    [StringLength(64)]
    public string? SubscriptionId { get; set; }
    
    [StringLength(16)]
    public string? Language { get; set; }
    
    public bool TotpEnabled { get; set; }
    
    [StringLength(128)]
    public string? TotpSecret { get; set; }
    
    [StringLength(64)]
    public string? PasswordSalt { get; set; }
}
