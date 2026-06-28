using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// One item on one side of a trade proposal (see <see cref="DbTransactionProposal"/>). The
/// <see cref="Direction"/> records whether the proposing app offers the item to the user
/// (app → user) or requests it from the user (user → app). Ownership is only moved when the
/// user approves the proposal.
/// </summary>
[Index(nameof(ProposalId))]
public class DbTransactionProposalItem {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(128)]
    [ForeignKey(nameof(ProposalNavigation))]
    public string ProposalId { get; set; } = null!;

    [StringLength(64)]
    public string ItemId { get; set; } = null!;

    /// <summary>Stored as the int value of <c>ProposalItemDirection</c> (Offer = 0, Request = 1).</summary>
    public int Direction { get; set; }

    // navigation property
    public DbTransactionProposal? ProposalNavigation { get; set; }
}
