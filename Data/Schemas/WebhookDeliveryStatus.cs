namespace SerbleAPI.Data.Schemas;

/// <summary>
/// Lifecycle of one outbox row. Stored as an int so states can be appended without rewriting
/// existing rows.
/// </summary>
public enum WebhookDeliveryStatus {
    /// <summary>Due for delivery once <c>NextAttemptUtc</c> passes. The only status the dispatcher claims from.</summary>
    Pending = 0,

    /// <summary>
    /// Claimed by a dispatcher instance and currently being sent. Claiming flips the status, so two
    /// replicas polling the same batch cannot both take the same row. A claim left behind by a
    /// crashed process is returned to <see cref="Pending"/> by the stale-claim sweep.
    /// </summary>
    InFlight = 1,

    /// <summary>The receiver answered 2xx.</summary>
    Delivered = 2,

    /// <summary>Gave up after exhausting the retry budget. Kept for inspection and manual redelivery.</summary>
    DeadLettered = 3
}
