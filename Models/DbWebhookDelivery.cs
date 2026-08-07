using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SerbleAPI.Models;

/// <summary>
/// One queued webhook delivery — the transactional outbox that keeps HTTP out of the transactions
/// that move coins.
/// <para>
/// A row is written in the <i>same</i> transaction as the mutation it describes, so an event can
/// never describe work that rolled back and committed work can never fail to produce its event.
/// The dispatcher then drains this table out-of-band; an unreachable app endpoint costs retries
/// here and nothing at all in the economy.
/// </para>
/// <para>
/// <see cref="DedupeKey"/> is unique per subscription, which makes enqueue idempotent: a resumed
/// tax cycle re-entering distribution cannot queue an event twice, and the receiver can dedupe on
/// <see cref="Id"/> across redeliveries of the same event.
/// </para>
/// </summary>
[Index(nameof(Status), nameof(NextAttemptUtc))]
[Index(nameof(AppId), nameof(CreatedUtc))]
[Index(nameof(WebhookId), nameof(CreatedUtc))]
[Index(nameof(WebhookId), nameof(DedupeKey), IsUnique = true)]
[Index(nameof(CycleId))]
public class DbWebhookDelivery {
    /// <summary>Also the event id sent to the receiver, stable across every redelivery attempt.</summary>
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = null!;

    [StringLength(64)]
    [ForeignKey(nameof(WebhookNavigation))]
    public string WebhookId { get; set; } = null!;

    /// <summary>Denormalised from the subscription so admin queries and per-app listings never need a join.</summary>
    [StringLength(64)]
    public string AppId { get; set; } = null!;

    [StringLength(64)]
    public string EventType { get; set; } = null!;

    /// <summary>The tax cycle this event came from, or null for events with no cycle (e.g. a test).</summary>
    public long? CycleId { get; set; }

    /// <summary>
    /// Identity of the event within its subscription, e.g. <c>tax.payout:412</c>. Unique per
    /// subscription — this is what makes enqueue safe to retry.
    /// </summary>
    [StringLength(128)]
    public string DedupeKey { get; set; } = null!;

    /// <summary>
    /// The exact JSON body to POST, serialised at enqueue time. Stored rather than regenerated so
    /// the signature covers bytes that cannot drift between attempts.
    /// </summary>
    public string Payload { get; set; } = "";

    /// <summary>Maps to <see cref="Data.Schemas.WebhookDeliveryStatus"/>.</summary>
    public int Status { get; set; }

    public int Attempts { get; set; }

    /// <summary>When this row next becomes claimable. Half of the dispatcher's poll index.</summary>
    public DateTime NextAttemptUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? DeliveredUtc { get; set; }

    public int? LastResponseCode { get; set; }

    [StringLength(512)]
    public string? LastError { get; set; }

    /// <summary>Dispatcher instance holding the current claim. Null unless in flight.</summary>
    [StringLength(128)]
    public string? ClaimedBy { get; set; }

    /// <summary>When the claim was taken. A claim older than the sweep window is treated as abandoned.</summary>
    public DateTime? ClaimedUtc { get; set; }

    // navigation properties
    public DbAppWebhook WebhookNavigation { get; set; } = null!;
}
