using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Repositories;

/// <summary>A webhook subscription as shown to its owner. Never carries the signing secret.</summary>
public class AppWebhookInfo {
    public string Id { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Url { get; set; } = "";
    public string[] EventTypes { get; set; } = [];
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime DateUpdated { get; set; }
}

/// <summary>A subscription plus its signing secret. Returned only at creation and on rotation.</summary>
public class AppWebhookWithSecret {
    public AppWebhookInfo Info { get; set; } = new();
    public string Secret { get; set; } = "";
}

/// <summary>One delivery attempt record, for an owner or admin diagnosing a failing endpoint.</summary>
public class WebhookDeliveryInfo {
    public string Id { get; set; } = "";
    public string WebhookId { get; set; } = "";
    public string AppId { get; set; } = "";
    public string EventType { get; set; } = "";
    public long? CycleId { get; set; }
    public WebhookDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public int? LastResponseCode { get; set; }
    public string? LastError { get; set; }

    /// <summary>The exact JSON body. Included for admins and for the owning app; both are entitled to see it.</summary>
    public string Payload { get; set; } = "";
}

public class WebhookDeliveryPage {
    public List<WebhookDeliveryInfo> Deliveries { get; init; } = [];
    public long TotalCount { get; init; }
}

/// <summary>Fields a subscription update may change. Null means "leave as is".</summary>
public class AppWebhookUpdate {
    public string? Url { get; init; }
    public IReadOnlyCollection<string>? EventTypes { get; init; }
    public bool? Enabled { get; init; }
}

public interface IAppWebhookRepository {
    Task<AppWebhookInfo[]> GetForApp(string appId);
    Task<AppWebhookInfo?> Get(string appId, string webhookId);
    Task<int> CountForApp(string appId);

    /// <summary>Creates a subscription. The signing secret is returned only here.</summary>
    Task<AppWebhookWithSecret> Create(string appId, string url, IEnumerable<string> eventTypes);

    /// <summary>
    /// Applies an update. Re-enabling clears the auto-disable state, since the owner is asserting
    /// the endpoint is fixed and the old failure count would otherwise disable it again immediately.
    /// </summary>
    Task<AppWebhookInfo?> Update(string appId, string webhookId, AppWebhookUpdate update);

    Task<bool> Delete(string appId, string webhookId);

    /// <summary>Issues a new signing secret, invalidating the old one. Returns null if not found.</summary>
    Task<AppWebhookWithSecret?> RotateSecret(string appId, string webhookId);

    /// <summary>Queues a <c>webhook.test</c> event to one subscription. Returns the delivery id, or null if not found.</summary>
    Task<string?> EnqueueTest(string appId, string webhookId);

    /// <summary>Deliveries for one subscription, newest first.</summary>
    Task<WebhookDeliveryPage> GetDeliveries(string appId, string? webhookId, int skip, int take);

    // ---- admin ----

    Task<AppWebhookInfo[]> GetAll(string? appId, int skip, int take);
    Task<long> CountAll(string? appId);

    /// <summary>Every delivery, optionally filtered. Admin view; not scoped to one app.</summary>
    Task<WebhookDeliveryPage> GetAllDeliveries(string? appId, WebhookDeliveryStatus? status, long? cycleId, int skip, int take);

    /// <summary>
    /// Puts a delivery back on the queue for immediate re-sending, whatever state it was in.
    /// Resets the attempt budget so a dead-lettered row gets a fresh set of retries.
    /// </summary>
    Task<bool> Redeliver(string deliveryId);
}
