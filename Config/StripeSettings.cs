namespace SerbleAPI.Config;

public class StripeSettings {
    public string ApiKey { get; set; } = null!;
    public string WebhookSecret { get; set; } = null!;
    public string PremiumSubscriptionId { get; set; } = null!;
}
