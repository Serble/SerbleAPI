using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// A coin balance owned by some entity (user or app). Each balance has its own id so a
/// single entity can own multiple balances in the future. The (OwnerType, OwnerId) pair is
/// indexed (non-unique) for lookups; currently each owner has a single "default" balance.
/// </summary>
[Index(nameof(OwnerType), nameof(OwnerId))]
public class DbBalance {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    public int OwnerType { get; set; }
    
    [StringLength(64)]
    public string OwnerId { get; set; } = null!;
    
    public ulong Coins { get; set; }
    
    public DateTime DateCreated { get; set; }
}
