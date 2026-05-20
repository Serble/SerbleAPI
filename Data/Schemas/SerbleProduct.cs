using System.Text.Json;
using System.Text.Json.Serialization;
using SerbleAPI.Models;

namespace SerbleAPI.Data.Schemas;

public class SerbleProduct {
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Id { get; set; } = null!;
    public string[] PriceIds { get; set; } = null!;
    public Dictionary<string, string> PriceLookupIds { get; set; } = null!;
    public bool Purchasable { get; set; }
    public string? SuccessRedirect { get; set; } = null;

    [JsonIgnore]
    public string? SuccessTokenSecret { get; set; } = null;

    public string? Webhook { get; set; } = null;

    [JsonIgnore]
    public string? WebhookSecret { get; set; } = null;

    public bool AllowAnonymous { get; set; }

    public static SerbleProduct FromDb(DbSerbleProduct row) {
        return new SerbleProduct {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            PriceIds = JsonSerializer.Deserialize<string[]>(row.PriceIdsJson) ?? [],
            PriceLookupIds = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PriceLookupIdsJson)
                ?? new Dictionary<string, string>(),
            Purchasable = row.Purchasable,
            SuccessRedirect = row.SuccessRedirect,
            SuccessTokenSecret = row.SuccessTokenSecret,
            Webhook = row.Webhook,
            WebhookSecret = row.WebhookSecret,
            AllowAnonymous = row.AllowAnonymous
        };
    }
}