using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// One item on one side of a user-to-user trade (see <see cref="DbUserTrade"/>). The
/// <see cref="Direction"/> records whether the initiator offers it to the recipient
/// (Offer = from → to) or requests it from the recipient (Request = to → from). Reuses the same
/// int values as <c>ProposalItemDirection</c> (Offer = 0, Request = 1). Ownership only moves when
/// the recipient approves.
/// </summary>
[Index(nameof(TradeId))]
public class DbUserTradeItem {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(128)]
    [ForeignKey(nameof(TradeNavigation))]
    public string TradeId { get; set; } = null!;

    [StringLength(64)]
    public string ItemId { get; set; } = null!;

    /// <summary>Stored as the int value of <c>ProposalItemDirection</c> (Offer = 0, Request = 1).</summary>
    public int Direction { get; set; }

    // navigation property
    public DbUserTrade? TradeNavigation { get; set; }
}
