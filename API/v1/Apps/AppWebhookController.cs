using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Config;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;
using SerbleAPI.Services;

namespace SerbleAPI.API.v1.Apps;

/// <summary>
/// An app manages its own event subscriptions here, authenticated with its API key.
/// <para>
/// Deliberately app-key-only rather than owner-token-managed: the signing secret is the whole point
/// of a subscription, and it belongs on the app's server, not in whatever browser session an owner
/// happens to be using. The app that will verify the signatures is the one that asks for them.
/// </para>
/// <para>
/// Not gated on the economy feature flag. The tax events it carries only exist when the economy is
/// on, but an app must still be able to inspect and clean up its subscriptions when it is off.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/app/me/webhooks")]
[Authorize(Policy = "AppOnly")]
[RateLimit(RateLimitTiers.Write)]
public class AppWebhookController(
    IAppWebhookRepository webhookRepo,
    IAppRepository appRepo,
    IServerConfigService config,
    ILogger<AppWebhookController> logger) : ControllerManager {

    public class WebhookView {
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

        public static WebhookView From(AppWebhookInfo w) => new() {
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

    /// <summary>A subscription plus its signing secret. Only returned by create and rotate.</summary>
    public class WebhookWithSecretView : WebhookView {
        public string Secret { get; set; } = "";

        public static WebhookWithSecretView From(AppWebhookWithSecret w) {
            WebhookView view = WebhookView.From(w.Info);
            return new WebhookWithSecretView {
                Id                  = view.Id,
                AppId               = view.AppId,
                Url                 = view.Url,
                EventTypes          = view.EventTypes,
                Enabled             = view.Enabled,
                DisabledReason      = view.DisabledReason,
                ConsecutiveFailures = view.ConsecutiveFailures,
                LastSuccessUtc      = view.LastSuccessUtc,
                LastFailureUtc      = view.LastFailureUtc,
                LastError           = view.LastError,
                DateCreated         = view.DateCreated,
                DateUpdated         = view.DateUpdated,
                Secret              = w.Secret
            };
        }
    }

    public class DeliveryView {
        public string Id { get; set; } = "";
        public string WebhookId { get; set; } = "";
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

        public static DeliveryView From(WebhookDeliveryInfo d) => new() {
            Id               = d.Id,
            WebhookId        = d.WebhookId,
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

    public class DeliveryPageView {
        public long TotalCount { get; set; }
        public List<DeliveryView> Deliveries { get; set; } = [];
    }

    public class CreateWebhookBody {
        public string Url { get; set; } = "";

        /// <summary>Event slugs to subscribe to. At least one must be a known slug.</summary>
        public string[] EventTypes { get; set; } = [];
    }

    public class UpdateWebhookBody {
        public string? Url { get; set; }
        public string[]? EventTypes { get; set; }
        public bool? Enabled { get; set; }
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The event types that can actually reach this app, so an integration can render them. Every
    /// app sees <c>tax.collected</c>; official apps additionally see <c>tax.payout</c>, which can
    /// never fire for anyone else.
    /// </summary>
    [HttpGet("event-types")]
    public async Task<IActionResult> GetEventTypes() {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        OAuthApp? app = await appRepo.GetOAuthApp(appId);
        if (app == null) return NotFound();
        return Ok(WebhookEventTypes.ForApp(app.IsOfficial).Select(e => new { eventType = e }));
    }

    [HttpGet]
    public async Task<ActionResult<WebhookView[]>> List() {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        AppWebhookInfo[] hooks = await webhookRepo.GetForApp(appId);
        return Ok(hooks.Select(WebhookView.From).ToArray());
    }

    [HttpGet("{webhookId}")]
    public async Task<ActionResult<WebhookView>> Get(string webhookId) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        AppWebhookInfo? hook = await webhookRepo.Get(appId, webhookId);
        return hook == null ? NotFound() : Ok(WebhookView.From(hook));
    }

    /// <summary>
    /// Registers an endpoint. The signing secret is in the response and is never shown again —
    /// losing it means rotating it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WebhookWithSecretView>> Create([FromBody] CreateWebhookBody body) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        string[] eventTypes = NormaliseEventTypes(body.EventTypes, out string? eventError);
        if (eventError != null) return BadRequest(eventError);

        (string? url, ActionResult? urlError) = await ValidateUrl(body.Url);
        if (urlError != null) return urlError;

        ulong max = await GetInteger(ServerConfigCatalog.WebhooksMaxPerApp);
        int existing = await webhookRepo.CountForApp(appId);
        if ((ulong)existing >= max) {
            return BadRequest(max == 0
                ? "Webhook registration is disabled on this server."
                : $"This app already has the maximum of {max} webhook(s). Delete one before adding another.");
        }

        AppWebhookInfo[] current = await webhookRepo.GetForApp(appId);
        if (current.Any(w => string.Equals(w.Url, url, StringComparison.Ordinal))) {
            return BadRequest("This app already has a webhook registered for that URL.");
        }

        AppWebhookWithSecret created = await webhookRepo.Create(appId, url!, eventTypes);
        logger.LogInformation("App {AppId} registered webhook {WebhookId} for {EventTypes}",
            appId, created.Info.Id, string.Join(',', eventTypes));
        return Ok(WebhookWithSecretView.From(created));
    }

    [HttpPatch("{webhookId}")]
    public async Task<ActionResult<WebhookView>> Update(string webhookId, [FromBody] UpdateWebhookBody body) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        string[]? eventTypes = null;
        if (body.EventTypes != null) {
            eventTypes = NormaliseEventTypes(body.EventTypes, out string? eventError);
            if (eventError != null) return BadRequest(eventError);
        }

        string? url = null;
        if (body.Url != null) {
            (url, ActionResult? urlError) = await ValidateUrl(body.Url);
            if (urlError != null) return urlError;
        }

        AppWebhookInfo? updated = await webhookRepo.Update(appId, webhookId, new AppWebhookUpdate {
            Url = url,
            EventTypes = eventTypes,
            Enabled = body.Enabled
        });
        return updated == null ? NotFound() : Ok(WebhookView.From(updated));
    }

    [HttpDelete("{webhookId}")]
    public async Task<IActionResult> Delete(string webhookId) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        bool deleted = await webhookRepo.Delete(appId, webhookId);
        if (!deleted) return NotFound();
        logger.LogInformation("App {AppId} deleted webhook {WebhookId}", appId, webhookId);
        return Ok(new { success = true });
    }

    /// <summary>Issues a new signing secret. The previous one stops verifying immediately.</summary>
    [HttpPost("{webhookId}/rotate-secret")]
    public async Task<ActionResult<WebhookWithSecretView>> RotateSecret(string webhookId) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        AppWebhookWithSecret? rotated = await webhookRepo.RotateSecret(appId, webhookId);
        if (rotated == null) return NotFound();
        logger.LogInformation("App {AppId} rotated the secret for webhook {WebhookId}", appId, webhookId);
        return Ok(WebhookWithSecretView.From(rotated));
    }

    /// <summary>
    /// Queues a <c>webhook.test</c> event to this subscription so an integration can be verified
    /// end to end without waiting for a tax cycle. Delivered regardless of what the subscription
    /// subscribes to.
    /// </summary>
    // Queues an outbound request to a URL the caller chose.
    [RateLimit(RateLimitTiers.Costly)]
    [HttpPost("{webhookId}/test")]
    public async Task<IActionResult> Test(string webhookId) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();

        string? deliveryId = await webhookRepo.EnqueueTest(appId, webhookId);
        return deliveryId == null
            ? NotFound("No enabled webhook with that id.")
            : Ok(new { queued = true, deliveryId });
    }

    /// <summary>
    /// Recent deliveries for this app, newest first — the app's own view of what was sent, what
    /// came back and when the next retry is due.
    /// </summary>
    [HttpGet("deliveries")]
    public Task<ActionResult<DeliveryPageView>> ListDeliveries([FromQuery] int skip = 0, [FromQuery] int take = 50) =>
        Deliveries(null, skip, take);

    [HttpGet("{webhookId}/deliveries")]
    public Task<ActionResult<DeliveryPageView>> ListWebhookDeliveries(
        string webhookId, [FromQuery] int skip = 0, [FromQuery] int take = 50) =>
        Deliveries(webhookId, skip, take);

    private async Task<ActionResult<DeliveryPageView>> Deliveries(string? webhookId, int skip, int take) {
        string? appId = HttpContext.User.GetAppId();
        if (appId == null) return Unauthorized();
        if (skip < 0) return BadRequest("skip must not be negative.");
        if (take is < 1 or > 200) return BadRequest("take must be between 1 and 200.");

        WebhookDeliveryPage page = await webhookRepo.GetDeliveries(appId, webhookId, skip, take);
        return Ok(new DeliveryPageView {
            TotalCount = page.TotalCount,
            Deliveries = page.Deliveries.Select(DeliveryView.From).ToList()
        });
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Shared with <see cref="AppOwnerWebhookController"/> — the owner-facing endpoints must accept
    /// exactly the same slugs as the app-key ones, so the validation lives in one place.
    /// </summary>
    internal static string[] NormaliseEventTypes(string[]? requested, out string? error) {
        error = null;
        string[] cleaned = (requested ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string[] unknown = cleaned.Where(e => !WebhookEventTypes.IsSubscribable(e)).ToArray();
        if (unknown.Length > 0) {
            error = $"Unknown event type(s): {string.Join(", ", unknown)}. " +
                    $"Supported: {string.Join(", ", WebhookEventTypes.Subscribable)}.";
            return [];
        }
        if (cleaned.Length == 0) {
            error = $"At least one event type is required. Supported: {string.Join(", ", WebhookEventTypes.Subscribable)}.";
            return [];
        }
        return cleaned;
    }

    private async Task<(string? Url, ActionResult? Error)> ValidateUrl(string? raw) {
        bool allowInsecure = await GetBoolean(ServerConfigCatalog.WebhooksAllowInsecureUrls);
        if (!WebhookUrl.TryValidateFormat(raw, allowInsecure, out string url, out string? formatError)) {
            return (null, BadRequest(formatError));
        }

        if (!await GetBoolean(ServerConfigCatalog.WebhooksAllowPrivateHosts)) {
            string? hostError = await WebhookUrl.CheckHostIsPublic(url, HttpContext.RequestAborted);
            if (hostError != null) return (null, BadRequest(hostError));
        }

        return (url, null);
    }

    private async Task<bool> GetBoolean(string key) {
        ServerConfigItem? item = await config.Get(key);
        return bool.TryParse(item?.Value, out bool parsed) && parsed;
    }

    private async Task<ulong> GetInteger(string key) {
        ServerConfigItem? item = await config.Get(key);
        return ulong.TryParse(item?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 0;
    }
}
