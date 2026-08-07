using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Admin;

/// <summary>
/// Operator visibility into app webhooks: who is subscribed, what has been queued, and what failed.
/// <para>
/// The delivery log is the only place a failing integration is diagnosable from the server side —
/// the app owner sees their own deliveries, but an operator investigating "the economy is telling
/// apps the wrong thing" needs the view across every app.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/admin/webhooks")]
[Authorize(Policy = "AdminOnly")]
public class AdminWebhooksController(
    IAppWebhookRepository webhookRepo,
    ILogger<AdminWebhooksController> logger) : ControllerManager {

    /// <summary>Admin view of a subscription. Excludes the signing secret — only the owning app ever sees that.</summary>
    public class AdminWebhookView {
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

        public static AdminWebhookView From(AppWebhookInfo w) => new() {
            Id                  = w.Id,
            AppId               = w.AppId,
            Url                 = w.Url,
            EventTypes          = w.EventTypes,
            Enabled             = w.Enabled,
            DisabledReason      = w.DisabledReason,
            ConsecutiveFailures = w.ConsecutiveFailures,
            LastSuccessUtc      = w.LastSuccessUtc,
            LastFailureUtc      = w.LastFailureUtc,
            LastError           = w.LastError,
            DateCreated         = w.DateCreated,
            DateUpdated         = w.DateUpdated
        };
    }

    public class AdminWebhookPageView {
        public long TotalCount { get; set; }
        public List<AdminWebhookView> Webhooks { get; set; } = [];
    }

    public class AdminDeliveryView {
        public string Id { get; set; } = "";
        public string WebhookId { get; set; } = "";
        public string AppId { get; set; } = "";
        public string EventType { get; set; } = "";
        public long? CycleId { get; set; }
        public string Status { get; set; } = "";
        public int Attempts { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime NextAttemptUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }
        public int? LastResponseCode { get; set; }
        public string? LastError { get; set; }
        public string Payload { get; set; } = "";

        public static AdminDeliveryView From(WebhookDeliveryInfo d) => new() {
            Id               = d.Id,
            WebhookId        = d.WebhookId,
            AppId            = d.AppId,
            EventType        = d.EventType,
            CycleId          = d.CycleId,
            Status           = d.Status.ToString(),
            Attempts         = d.Attempts,
            CreatedUtc       = d.CreatedUtc,
            NextAttemptUtc   = d.NextAttemptUtc,
            DeliveredUtc     = d.DeliveredUtc,
            LastResponseCode = d.LastResponseCode,
            LastError        = d.LastError,
            Payload          = d.Payload
        };
    }

    public class AdminDeliveryPageView {
        public long TotalCount { get; set; }
        public List<AdminDeliveryView> Deliveries { get; set; } = [];
    }

    /// <summary>Every registered subscription, newest first, optionally filtered to one app.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminWebhookPageView>> ListWebhooks(
        [FromQuery] string? appId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50) {

        if (skip < 0) return BadRequest("skip must not be negative.");
        if (take is < 1 or > 200) return BadRequest("take must be between 1 and 200.");

        AppWebhookInfo[] hooks = await webhookRepo.GetAll(appId, skip, take);
        return Ok(new AdminWebhookPageView {
            TotalCount = await webhookRepo.CountAll(appId),
            Webhooks = hooks.Select(AdminWebhookView.From).ToList()
        });
    }

    /// <summary>
    /// The delivery outbox, newest first. Filter by <c>status=DeadLettered</c> to find integrations
    /// that gave up, or by <c>cycleId</c> to see everything one tax run emitted.
    /// </summary>
    [HttpGet("deliveries")]
    public async Task<ActionResult<AdminDeliveryPageView>> ListDeliveries(
        [FromQuery] string? appId = null,
        [FromQuery] string? status = null,
        [FromQuery] long? cycleId = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50) {

        if (skip < 0) return BadRequest("skip must not be negative.");
        if (take is < 1 or > 200) return BadRequest("take must be between 1 and 200.");

        WebhookDeliveryStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status)) {
            if (!Enum.TryParse(status, ignoreCase: true, out WebhookDeliveryStatus value)) {
                return BadRequest($"Unknown status. Expected one of: {string.Join(", ", Enum.GetNames<WebhookDeliveryStatus>())}.");
            }
            parsedStatus = value;
        }

        WebhookDeliveryPage page = await webhookRepo.GetAllDeliveries(appId, parsedStatus, cycleId, skip, take);
        return Ok(new AdminDeliveryPageView {
            TotalCount = page.TotalCount,
            Deliveries = page.Deliveries.Select(AdminDeliveryView.From).ToList()
        });
    }

    /// <summary>
    /// Requeues a delivery for immediate sending with a fresh attempt budget. The receiver sees the
    /// same event id as before, so an app that dedupes correctly is unharmed by a redelivery of
    /// something it already processed.
    /// </summary>
    [HttpPost("deliveries/{deliveryId}/redeliver")]
    public async Task<IActionResult> Redeliver(string deliveryId) {
        bool requeued = await webhookRepo.Redeliver(deliveryId);
        if (!requeued) return NotFound();
        logger.LogInformation("Admin {UserId} requeued webhook delivery {DeliveryId}",
            HttpContext.User.GetUserId(), deliveryId);
        return Ok(new { requeued = true });
    }
}
