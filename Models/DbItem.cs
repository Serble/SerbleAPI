using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// An owned object with metadata (name, description, icon) created by an app. A newly created
/// item is owned by the app that created it, but ownership can be either an App or a User (see
/// <see cref="SerbleAPI.Data.Schemas.BalanceOwnerType"/>, reused for owner-kind semantics).
/// Transferring ownership is not implemented yet.
/// </summary>
[Index(nameof(OwnerType), nameof(OwnerId))]
[Index(nameof(CreatorAppId))]
public class DbItem {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    /// <summary>Owner kind, stored as an int matching <c>BalanceOwnerType</c> (User = 0, App = 1).</summary>
    public int OwnerType { get; set; }

    [StringLength(64)]
    public string OwnerId { get; set; } = null!;

    /// <summary>The id of the app that created this item.</summary>
    [StringLength(64)]
    public string CreatorAppId { get; set; } = null!;

    public DateTime DateCreated { get; set; }

    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(1024)]
    public string? Description { get; set; }

    public string? IconUrl { get; set; }
}
