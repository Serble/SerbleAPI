namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Domain representation of an app-created item. The owner can be a <see cref="BalanceOwnerType.User"/>
/// or a <see cref="BalanceOwnerType.App"/>; newly created items are owned by their creating app.
/// </summary>
public class Item {
    public string Id { get; set; } = "";
    public BalanceOwnerType OwnerType { get; set; }
    public string OwnerId { get; set; } = "";
    public string CreatorAppId { get; set; } = "";
    public DateTime DateCreated { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}
