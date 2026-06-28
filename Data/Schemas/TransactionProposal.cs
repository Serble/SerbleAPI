namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of an app-proposed transaction awaiting (or having completed) a user's
/// consent. The consenting user is always the payer; the proposing app names the recipient.
/// </summary>
public class TransactionProposal {
    public string Id { get; set; } = "";
    /// <summary>The app that proposed the transaction.</summary>
    public string AppId { get; set; } = "";
    /// <summary>The user who must consent and whose balance is debited.</summary>
    public string UserId { get; set; } = "";
    /// <summary>Where the money goes.</summary>
    public BalanceOwnerType RecipientType { get; set; }
    public string RecipientId { get; set; } = "";
    /// <summary>Coins the user pays the recipient (user → recipient). 0 when nothing is requested.</summary>
    public ulong Amount { get; set; }
    /// <summary>Coins the proposing app offers the user (app → user). 0 when nothing is offered.</summary>
    public ulong OfferedCoins { get; set; }
    /// <summary>Ids of items the proposing app offers the user (app → user).</summary>
    public List<string> OfferedItemIds { get; set; } = new();
    /// <summary>Ids of items the proposing app requests from the user (user → app).</summary>
    public List<string> RequestedItemIds { get; set; } = new();
    public string? Description { get; set; }
    /// <summary>Optional URL to send the user back to after they decide (registered on the app).</summary>
    public string? RedirectUri { get; set; }
    public TransactionProposalStatus Status { get; set; }
    /// <summary>Set once the transfer succeeds.</summary>
    public string? TransactionId { get; set; }
    /// <summary>Set when <see cref="Status"/> is <see cref="TransactionProposalStatus.Failed"/>.</summary>
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
