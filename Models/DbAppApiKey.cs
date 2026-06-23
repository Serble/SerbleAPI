using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// An API key that lets an app authenticate as itself (format <c>sap_...</c>). Only a SHA-256
/// hash of the key is stored; the plaintext is shown once at creation. <see cref="KeyPrefix"/>
/// is a non-secret preview used for listing keys.
/// </summary>
[Index(nameof(KeyHash), IsUnique = true)]
public class DbAppApiKey {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    [StringLength(64)]
    [ForeignKey(nameof(AppNavigation))]
    public string AppId { get; set; } = null!;
    
    [StringLength(128)]
    public string Name { get; set; } = null!;
    
    [StringLength(128)]
    public string KeyHash { get; set; } = null!;
    
    [StringLength(32)]
    public string KeyPrefix { get; set; } = null!;
    
    public DateTime DateCreated { get; set; }
    
    // navigation properties
    public DbApp AppNavigation { get; set; } = null!;
}
