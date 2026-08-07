namespace SerbleAPI.Services;

/// <summary>
/// Drains the webhook outbox. Everything here runs outside the transactions that produced the
/// events, so a slow or unreachable subscriber costs retries and nothing else.
/// </summary>
public interface IWebhookDeliveryService {
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due deliveries, POSTs them, and records the
    /// outcome. Returns how many were attempted — a full batch means more work is waiting, so the
    /// caller can loop instead of sleeping.
    /// </summary>
    Task<int> DispatchDue(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns deliveries whose claim outlived the process that took it. Without this, a replica
    /// killed mid-POST would strand its batch in flight forever.
    /// </summary>
    Task<int> ReleaseStaleClaims(CancellationToken cancellationToken = default);
}
