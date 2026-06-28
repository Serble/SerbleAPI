using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// A trade proposed by one user to another, awaiting (or having resolved) the recipient's
/// decision. The id is a high-entropy random handle. See <c>UserTrade</c> for the domain shape.
/// </summary>
[Index(nameof(FromUserId))]
[Index(nameof(ToUserId))]
[Index(nameof(Status))]
public class DbUserTrade {
    [Key]
    [StringLength(128)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    public string FromUserId { get; set; } = null!;

    [StringLength(64)]
    public string ToUserId { get; set; } = null!;

    /// <summary>Coins the initiator gives the recipient (from → to).</summary>
    public ulong OfferedCoins { get; set; }

    /// <summary>Coins the initiator asks from the recipient (to → from).</summary>
    public ulong RequestedCoins { get; set; }

    [StringLength(256)]
    public string? Description { get; set; }

    /// <summary>Stored as the int value of <c>UserTradeStatus</c>.</summary>
    public int Status { get; set; }

    [StringLength(64)]
    public string? TransactionId { get; set; }

    [StringLength(256)]
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
