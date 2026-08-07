using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// An app's subscription to server events: where to POST them and which ones to send. Owned by the
/// app and managed with its API key, because <see cref="Secret"/> is what the app's server needs to
/// verify signatures — it should never travel through a browser session.
/// <para>
/// Deleting the app deletes its subscriptions, and deleting a subscription deletes its outbox rows
/// (<see cref="DbWebhookDelivery"/>), so an unsubscribe also stops any deliveries still queued.
/// </para>
/// </summary>
[Index(nameof(AppId))]
[Index(nameof(AppId), nameof(Url), IsUnique = true)]
public class DbAppWebhook {
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(AppNavigation))]
    public string AppId { get; set; } = null!;

    /// <summary>Absolute endpoint the dispatcher POSTs to. Unique per app, so one URL cannot be double-registered.</summary>
    [StringLength(512)]
    public string Url { get; set; } = null!;

    /// <summary>
    /// HMAC-SHA256 signing key, stored in plaintext because signing requires the original bytes.
    /// Revealed to the owner at creation and on rotation only.
    /// </summary>
    [StringLength(128)]
    public string Secret { get; set; } = null!;

    /// <summary>Comma-separated <see cref="Data.Schemas.WebhookEventTypes"/> slugs.</summary>
    [StringLength(256)]
    public string EventTypes { get; set; } = "";

    /// <summary>
    /// Whether the dispatcher will queue new events for this subscription. Cleared automatically
    /// when an endpoint fails for long enough (see <see cref="DisabledReason"/>) so a permanently
    /// dead URL stops consuming delivery attempts.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Why the subscription was auto-disabled, or null if it was disabled by its owner or is enabled.</summary>
    [StringLength(256)]
    public string? DisabledReason { get; set; }

    /// <summary>
    /// Failed attempts since the last success. Reset on any 2xx. Drives auto-disable, and is the
    /// number an app owner looks at to tell "my endpoint is flaky" from "my endpoint is gone".
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    public DateTime? LastSuccessUtc { get; set; }

    public DateTime? LastFailureUtc { get; set; }

    [StringLength(512)]
    public string? LastError { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateUpdated { get; set; }

    // navigation properties
    public DbApp AppNavigation { get; set; } = null!;
}
