using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbUserPasskey {
    [Key]
    public int Id { get; set; }
    
    [ForeignKey(nameof(OwnerNavigation))]
    public string OwnerId { get; set; } = null!;
    
    public string? Name { get; set; }
    
    public string? CredentialId { get; set; }
    
    public string? PublicKey { get; set; }
    
    public int? SignCount { get; set; }
    
    public string? AaGuid { get; set; }
    
    public string? AttesClientDataJson { get; set; }
    
    public int? DescriptorType { get; set; }
    
    public string? DescriptorId { get; set; }
    
    public int? DescriptorTransports { get; set; }
    
    public string? AttesFormat { get; set; }
    
    public int? Transports { get; set; }
    
    public bool? BackupEligible { get; set; }
    
    public bool? BackedUp { get; set; }
    
    public string? AttesObject { get; set; }
    
    public string? DevicePublicKeys { get; set; }
    
    public DateTime? CreatedAt { get; set; }
    
    // navigation properties
    public DbUser OwnerNavigation { get; set; } = null!;
}
