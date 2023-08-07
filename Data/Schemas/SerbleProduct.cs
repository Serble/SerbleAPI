namespace SerbleAPI.Data.Schemas; 

public class SerbleProduct {
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string[] PriceIds { get; set; } = null!;
    public Dictionary<string, string> PriceLookupIds { get; set; } = null!;
    public bool Purchasable { get; set; }
}