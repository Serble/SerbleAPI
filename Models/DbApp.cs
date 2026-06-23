using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SerbleAPI.Models;

public class DbApp {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;
    
    [StringLength(64)]
    [ForeignKey(nameof(OwnerNavigation))]
    public string OwnerId { get; set; } = null!;
    
    [StringLength(64)]
    public string Name { get; set; } = null!;
    
    [StringLength(1024)]
    public string Description { get; set; } = null!;
    
    [StringLength(64)]
    public string ClientSecret { get; set; } = null!;
    
    public string RedirectUri { get; set; } = null!;
    
    /// <summary>Additional OIDC redirect URIs as a JSON string array (legacy RedirectUri still applies).</summary>
    public string? OidcRedirectUris { get; set; }
    
    /// <summary>Public (secret-less) client — must use PKCE instead of a client secret.</summary>
    public bool IsPublicClient { get; set; }
    
    /// <summary>Require a PKCE challenge even for confidential clients.</summary>
    public bool RequirePkce { get; set; }
    
    /// <summary>Admin-only flag marking the app as an official, first-party application.</summary>
    public bool IsOfficial { get; set; }
    
    /// <summary>Admin-only access gate. Maps to <see cref="Data.Schemas.AppAccessPolicy"/>.</summary>
    public int AccessPolicy { get; set; }
    
    /// <summary>Admin-only minimum PermLevel when AccessPolicy = RequireMinimumPermLevel.</summary>
    public int? RequiredPermLevel { get; set; }
    
    public DateTime DateCreated { get; set; }
    
    // navigation properties
    public DbUser OwnerNavigation { get; set; } = null!;
}
