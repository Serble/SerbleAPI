namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of a coin balance owned by a user or app.
/// </summary>
public class Balance {
    public string Id { get; set; } = "";
    public BalanceOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = "";
    public ulong Coins { get; set; }
    public DateTime DateCreated { get; set; }
}
