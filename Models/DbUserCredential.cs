using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>One sign-in credential. Passkeys keep their key material in <see cref="DbUserPasskey"/>.</summary>
[Index(nameof(UserId), nameof(Type))]
public class DbUserCredential {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(UserNavigation))]
    public string UserId { get; set; } = null!;

    public int Type { get; set; }

    [StringLength(255)]
    public string? Name { get; set; }

    public int Status { get; set; }

    [StringLength(512)]
    public string? Secret { get; set; }

    public int Scheme { get; set; }

    [StringLength(64)]
    public string? LegacySalt { get; set; }

    /// <summary>For TOTP, the highest time step already accepted.</summary>
    public long? Counter { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DbUser UserNavigation { get; set; } = null!;
}
