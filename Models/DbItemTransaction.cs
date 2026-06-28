using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Models;

/// <summary>
/// An immutable audit record of a single item ownership change — an item's "chain of custody". A
/// genesis record (<see cref="ItemTransferKind.Created"/>) is written when an app mints an item, and
/// a further record is written every time the item moves through an approved trade.
/// <para>
/// Owner ids are stored as plain strings (no FK) so the history survives deletion of the owning user
/// or app. The item FK cascades, so an item's history is removed only when the item itself is.
/// </para>
/// </summary>
[Index(nameof(ItemId), nameof(DateCreated))]
public class DbItemTransaction {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(ItemNavigation))]
    public string ItemId { get; set; } = null!;

    /// <summary>Why the item moved; the int value of <c>ItemTransferKind</c>.</summary>
    public int Kind { get; set; }

    /// <summary>Previous owner kind (int <c>BalanceOwnerType</c>), or null for a genesis/mint record.</summary>
    public int? FromOwnerType { get; set; }

    [StringLength(64)]
    public string? FromOwnerId { get; set; }

    /// <summary>New owner kind; the int value of <c>BalanceOwnerType</c>.</summary>
    public int ToOwnerType { get; set; }

    [StringLength(64)]
    public string ToOwnerId { get; set; } = null!;

    /// <summary>The trade proposal that caused the move, when applicable.</summary>
    [StringLength(128)]
    public string? ProposalId { get; set; }

    public DateTime DateCreated { get; set; }

    // navigation property
    public DbItem? ItemNavigation { get; set; }

    /// <summary>Builds a genesis (mint) record: the item came into existence owned by its creator.</summary>
    public static DbItemTransaction Created(string itemId, BalanceOwnerType toType, string toId, DateTime when) => new() {
        Id          = Guid.NewGuid().ToString(),
        ItemId      = itemId,
        Kind        = (int)ItemTransferKind.Created,
        ToOwnerType = (int)toType,
        ToOwnerId   = toId,
        DateCreated = when
    };

    /// <summary>Builds a trade record: the item moved from one owner to another via a proposal.</summary>
    public static DbItemTransaction Trade(
        string itemId,
        BalanceOwnerType fromType, string fromId,
        BalanceOwnerType toType, string toId,
        string proposalId, DateTime when) => new() {
        Id            = Guid.NewGuid().ToString(),
        ItemId        = itemId,
        Kind          = (int)ItemTransferKind.Trade,
        FromOwnerType = (int)fromType,
        FromOwnerId   = fromId,
        ToOwnerType   = (int)toType,
        ToOwnerId     = toId,
        ProposalId    = proposalId,
        DateCreated   = when
    };
}
