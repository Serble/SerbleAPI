namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of a trade proposed by one user to another. Symmetric: the initiator
/// (<see cref="FromUserId"/>) offers coins/items to the recipient (<see cref="ToUserId"/>) and may
/// request coins/items back. The recipient approves or denies; on approval both sides move
/// atomically, so neither user has to trust the other to deliver their half.
/// </summary>
public class UserTrade {
    public string Id { get; set; } = "";
    /// <summary>The user who proposed the trade (gives the offered side, receives the requested side).</summary>
    public string FromUserId { get; set; } = "";
    /// <summary>The user who must approve (receives the offered side, gives the requested side).</summary>
    public string ToUserId { get; set; } = "";
    /// <summary>Coins the initiator gives the recipient (from → to). 0 when nothing is offered.</summary>
    public ulong OfferedCoins { get; set; }
    /// <summary>Coins the initiator asks from the recipient (to → from). 0 when nothing is requested.</summary>
    public ulong RequestedCoins { get; set; }
    /// <summary>Items the initiator gives the recipient (from → to); the initiator must own them.</summary>
    public List<string> OfferedItemIds { get; set; } = new();
    /// <summary>Items the initiator asks from the recipient (to → from); the recipient must own them.</summary>
    public List<string> RequestedItemIds { get; set; } = new();
    public string? Description { get; set; }
    public UserTradeStatus Status { get; set; }
    /// <summary>Set once the swap succeeds (one of the coin legs, for reference). Null for items-only.</summary>
    public string? TransactionId { get; set; }
    /// <summary>Set when <see cref="Status"/> is <see cref="UserTradeStatus.Failed"/>.</summary>
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>True when the initiator gives something and asks for nothing back (a gift).</summary>
    public bool IsGift => RequestedCoins == 0 && RequestedItemIds.Count == 0
                          && (OfferedCoins > 0 || OfferedItemIds.Count > 0);
}
