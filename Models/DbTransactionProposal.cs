using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// A transaction proposed by an app and awaiting (or having resolved) a user's consent on the
/// serble website, modelled on the OIDC consent flow. The consenting user is always the payer;
/// the proposing app names the recipient. The proposal id is a high-entropy random handle used
/// as the opaque consent-session token.
/// </summary>
[Index(nameof(AppId))]
[Index(nameof(UserId))]
public class DbTransactionProposal {
    [Key]
    [StringLength(128)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    public string AppId { get; set; } = null!;

    [StringLength(64)]
    public string UserId { get; set; } = null!;

    /// <summary>Recipient owner kind, stored as the int value of <c>BalanceOwnerType</c>.</summary>
    public int RecipientType { get; set; }

    [StringLength(64)]
    public string RecipientId { get; set; } = null!;

    public ulong Amount { get; set; }

    /// <summary>Coins the proposing app offers the user (app → user). 0 when nothing is offered.</summary>
    public ulong OfferedCoins { get; set; }

    [StringLength(256)]
    public string? Description { get; set; }

    /// <summary>Optional URL to send the user back to after they decide. Any URL is accepted;
    /// only the opaque proposal id and outcome status are appended to it. Null if the app
    /// supplied none.</summary>
    [StringLength(2048)]
    public string? RedirectUri { get; set; }

    /// <summary>Stored as the int value of <c>TransactionProposalStatus</c>.</summary>
    public int Status { get; set; }

    [StringLength(64)]
    public string? TransactionId { get; set; }

    [StringLength(256)]
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
