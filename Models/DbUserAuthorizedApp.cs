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
    
    /// <summary>
    /// What the grant permits, in the notation its <see cref="GrantType"/> names: a bitmask string
    /// for a legacy grant, a space-delimited scope list for an OIDC one.
    /// </summary>
    [StringLength(128)]
    public string Scopes { get; set; } = null!;

    /// <summary>
    /// Which flow produced the grant (<see cref="Data.Schemas.AuthorizedAppGrantType"/>); rows come
    /// from both. Defaults to the legacy flow, where every row predating OIDC consent came from.
    /// </summary>
    public int GrantType { get; set; }

    public DateTime DateCreated { get; set; }
    
    // navigation properties
    public DbUser UserNavigation { get; set; } = null!;
    public DbApp AppNavigation { get; set; } = null!;
}
