namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of one entry in an item's ownership history — an immutable audit record of
/// a single change of custody. A <see cref="ItemTransferKind.Created"/> genesis entry is written
/// when an app mints the item (no previous owner), and a <see cref="ItemTransferKind.Trade"/> entry
/// is written for every subsequent move through an approved trade.
/// </summary>
public class ItemTransaction {
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public ItemTransferKind Kind { get; set; }
    /// <summary>Previous owner kind, or null for a genesis/mint record.</summary>
    public BalanceOwnerType? FromOwnerType { get; set; }
    public string? FromOwnerId { get; set; }
    public BalanceOwnerType ToOwnerType { get; set; }
    public string ToOwnerId { get; set; } = "";
    /// <summary>The trade proposal that caused the move, when applicable.</summary>
    public string? ProposalId { get; set; }
    public DateTime DateCreated { get; set; }
}
