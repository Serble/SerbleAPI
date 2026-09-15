using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// An account. <see cref="Username"/> is unique: it is an addressing key across the API (login,
/// lookups by name, trade and transaction recipients), so the database enforces uniqueness rather
/// than trusting the read-then-write checks in the register and account-edit paths, which two
/// concurrent requests can both pass. The index also backs those by-name lookups.
/// <para>
/// Uniqueness is case- and accent-insensitive, because it follows the column's MySQL collation --
/// the same collation the by-name lookups already compare under, so the constraint admits exactly
/// the names those lookups would have found.
/// </para>
/// </summary>
[Index(nameof(Username), IsUnique = true)]
public class DbUser {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    [StringLength(255)]
    public string Username { get; set; } = null!;
    
    [StringLength(255)]  // by standard, emails can be up to 254 characters long
    public string? Email { get; set; }
    
    // Password, PasswordSalt, TotpEnabled, TotpSecret and LastTotpCounter are superseded by
    // UserCredentials and are no longer read. They remain until a later migration drops them.

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
    
    public DateTime DateCreated { get; set; }

    public DateTime? LastLogin { get; set; }

    /// <summary>
    /// The instant before which tokens naming this account are no longer accepted, or null if none
    /// have been revoked. Tokens are self-contained, so this is what takes them back: the
    /// authentication handler refuses any issued earlier, retiring a whole generation with one
    /// write rather than a record of each.
    /// </summary>
    public DateTime? TokensValidFrom { get; set; }

    public long? LastTotpCounter { get; set; }
}
