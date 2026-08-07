using System.Text.Json;
using System.Text.Json.Serialization;

namespace SerbleAPI.Data.Schemas;

/// <summary>
/// The JSON body POSTed to a subscriber. One shape covers every event type; fields that do not
/// apply are omitted rather than sent as null, so receivers can switch on <see cref="Event"/> and
/// read only what that event defines.
/// <para>
/// Coin amounts are strings. They are raw fixed-point units (see
/// <see cref="SerbleAPI.Data.CoinFixedPoint"/>) and routinely exceed what a JSON number represents
/// exactly, so sending them as numbers would silently corrupt them in any receiver that parses
/// JSON into doubles — which is most of them.
/// </para>
/// <para>
/// Aggregates only. An event says what happened to <i>this app's</i> balance and at what rate;
/// it never carries per-user detail, which would leak one app's users' balances to another app.
/// </para>
/// </summary>
public class WebhookEventPayload {
    /// <summary>
    /// Stable id of this event, equal to the delivery row id. Constant across retries, so it is the
    /// key a receiver dedupes on.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>A <see cref="WebhookEventTypes"/> slug.</summary>
    public string Event { get; set; } = "";

    public string AppId { get; set; } = "";

    /// <summary>When the event was queued, which for tax events is when the cycle became final.</summary>
    public DateTime OccurredAt { get; set; }

    // ---- tax events ----

    /// <summary>The tax run this event describes. Together with <see cref="Event"/> it uniquely identifies the occurrence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CycleId { get; set; }

    /// <summary>The scheduled cycle boundary, or omitted for an admin-triggered run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ScheduledForUtc { get; set; }

    /// <summary>True when an admin triggered the run rather than the schedule.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Manual { get; set; }

    /// <summary>
    /// The rate the cycle charged, as a decimal percentage string (e.g. <c>"2.5"</c>). An app that
    /// holds coins on behalf of its own users applies this rate to each held sub-balance so the
    /// charge lands on the users whose coins it actually was.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RatePercent { get; set; }

    /// <summary>Coins moved by this event: taken for <c>tax.collected</c>, received for <c>tax.payout</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Amount { get; set; }

    /// <summary>The app's balance before the event, summed across its balances if it has more than one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BalanceBefore { get; set; }

    /// <summary>The app's balance after the event. Reconciling against this is more reliable than replaying the rate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BalanceAfter { get; set; }

    /// <summary>Free-text note. Only used by <c>webhook.test</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Renders the exact body to send. Serialised once at enqueue and stored, never regenerated —
    /// the signature covers these bytes, so re-serialising before a retry could invalidate it.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}
