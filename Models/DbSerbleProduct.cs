using System.ComponentModel.DataAnnotations;

namespace SerbleAPI.Models;

public class DbSerbleProduct {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(128)]
    public string Name { get; set; } = null!;

    [StringLength(1024)]
    public string Description { get; set; } = null!;

    // JSON-encoded string[] of Stripe price IDs.
    public string PriceIdsJson { get; set; } = "[]";

    // JSON-encoded Dictionary<string,string> of plan key -> Stripe price lookup key.
    public string PriceLookupIdsJson { get; set; } = "{}";

    public bool Purchasable { get; set; }

    public string? SuccessRedirect { get; set; }

    [StringLength(256)]
    public string? SuccessTokenSecret { get; set; }

    public string? Webhook { get; set; }

    [StringLength(256)]
    public string? WebhookSecret { get; set; }

    public bool AllowAnonymous { get; set; }
}
