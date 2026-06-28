namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Which side of a trade an item sits on within a <see cref="TransactionProposal"/>.
/// Stored as an int in the <c>TransactionProposalItems</c> table.
/// </summary>
public enum ProposalItemDirection {
    /// <summary>The proposing app offers the item to the consenting user (app → user).</summary>
    Offer = 0,
    /// <summary>The proposing app requests the item from the consenting user (user → app).</summary>
    Request = 1
}
