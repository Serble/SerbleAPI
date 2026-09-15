using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>A sign-in or re-authentication in progress. Keyed by the hash of the handle given to the client.</summary>
[Index(nameof(ExpiresAt))]
public class DbLoginSession {
    [Key]
    [StringLength(64)]
    public string IdHash { get; set; } = null!;

    /// <summary>Null until known: a usernameless passkey sign-in learns it from the assertion.</summary>
    [StringLength(64)]
    [ForeignKey(nameof(UserNavigation))]
    public string? UserId { get; set; }

    public int Purpose { get; set; }

    public int CompletedMask { get; set; }

    /// <summary>Pending passkey assertion options, as JSON.</summary>
    public string? PasskeyChallenge { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool Consumed { get; set; }

    public DbUser? UserNavigation { get; set; }
}
