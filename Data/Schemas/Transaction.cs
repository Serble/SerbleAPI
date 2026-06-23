namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of a coin transaction (an audit record of a zero-sum transfer
/// between two balances).
/// </summary>
public class Transaction {
    public string Id { get; set; } = "";
    public string? FromBalanceId { get; set; }
    public string? ToBalanceId { get; set; }
    public ulong Amount { get; set; }
    public string? Description { get; set; }
    public DateTime DateCreated { get; set; }
}
