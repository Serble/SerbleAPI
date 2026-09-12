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
/// The owner-facing twin of <see cref="AppWebhookController"/>: same operations, authenticated with
/// the owner's user token instead of the app's API key, so the dashboard can manage subscriptions.
/// <para>
/// The signing secret is revealed here exactly as an API key is at
/// <c>POST /app/{appid}/keys</c> — the owner who can mint a key that authenticates <i>as</i> the app
/// is already strictly more privileged than one who can read a signature secret, so withholding it
/// from the same session would buy nothing.
/// </para>
/// <para>
/// Not gated on the economy feature flag, for the same reason the app-key controller is not: the tax
/// events only exist when the economy is on, but subscriptions must stay inspectable and removable
/// when it is off.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/app/{appid}/webhooks")]
[Authorize(Policy = "Scope:ManageApps")]
[RateLimit(RateLimitTiers.Write)]
public class AppOwnerWebhookController(
    IAppRepository appRepo,
    IUserRepository userRepo,
    IAppWebhookRepository webhookRepo,
    IServerConfigService config,
    ILogger<AppOwnerWebhookController> logger) : ControllerManager {

    /// <summary>Loads an app and ensures the authenticated user owns it.</summary>
    private async Task<(OAuthApp? app, ActionResult? error)> GetOwnedApp(string appid) {
        User? user = await HttpContext.User.GetUser(userRepo);
        if (user == null) return (null, Unauthorized());
        OAuthApp? app = await appRepo.GetOAuthApp(appid);
        if (app == null) return (null, NotFound());
        if (app.OwnerId != user.Id) return (null, Forbid());
        return (app, null);
    }

    /// <summary>
    /// The event types that can actually reach this app. Every app sees <c>tax.collected</c>;
    /// official apps additionally see <c>tax.payout</c>, which no other app can ever receive.
    /// </summary>
    [HttpGet("event-types")]
    public async Task<IActionResult> GetEventTypes(string appid) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        return Ok(WebhookEventTypes.ForApp(app!.IsOfficial).Select(e => new { eventType = e }));
    }

    [HttpGet]
    public async Task<ActionResult<AppWebhookController.WebhookView[]>> List(string appid) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        AppWebhookInfo[] hooks = await webhookRepo.GetForApp(app!.Id);
        return Ok(hooks.Select(AppWebhookController.WebhookView.From).ToArray());
    }

    [HttpGet("{webhookId}")]
    public async Task<ActionResult<AppWebhookController.WebhookView>> Get(string appid, string webhookId) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        AppWebhookInfo? hook = await webhookRepo.Get(app!.Id, webhookId);
        return hook == null ? NotFound() : Ok(AppWebhookController.WebhookView.From(hook));
    }

    /// <summary>
    /// Registers an endpoint. The signing secret is in the response and is never shown again —
    /// losing it means rotating it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AppWebhookController.WebhookWithSecretView>> Create(
        string appid, [FromBody] AppWebhookController.CreateWebhookBody body) {
        (OAuthApp? app, ActionResult? ownerError) = await GetOwnedApp(appid);
        if (ownerError != null) return ownerError;

        string[] eventTypes = AppWebhookController.NormaliseEventTypes(body.EventTypes, out string? eventError);
        if (eventError != null) return BadRequest(eventError);

        (string? url, ActionResult? urlError) = await ValidateUrl(body.Url);
        if (urlError != null) return urlError;

        ulong max = await GetInteger(ServerConfigCatalog.WebhooksMaxPerApp);
        int existing = await webhookRepo.CountForApp(app!.Id);
        if ((ulong)existing >= max) {
            return BadRequest(max == 0
                ? "Webhook registration is disabled on this server."
                : $"This app already has the maximum of {max} webhook(s). Delete one before adding another.");
        }

        AppWebhookInfo[] current = await webhookRepo.GetForApp(app.Id);
        if (current.Any(w => string.Equals(w.Url, url, StringComparison.Ordinal))) {
            return BadRequest("This app already has a webhook registered for that URL.");
        }

        AppWebhookWithSecret created = await webhookRepo.Create(app.Id, url!, eventTypes);
        logger.LogInformation("User {UserId} registered webhook {WebhookId} for app {AppId} ({EventTypes})",
            HttpContext.User.GetUserId(), created.Info.Id, app.Id, string.Join(',', eventTypes));
        return Ok(AppWebhookController.WebhookWithSecretView.From(created));
    }

    [HttpPatch("{webhookId}")]
    public async Task<ActionResult<AppWebhookController.WebhookView>> Update(
        string appid, string webhookId, [FromBody] AppWebhookController.UpdateWebhookBody body) {
        (OAuthApp? app, ActionResult? ownerError) = await GetOwnedApp(appid);
        if (ownerError != null) return ownerError;

        string[]? eventTypes = null;
        if (body.EventTypes != null) {
            eventTypes = AppWebhookController.NormaliseEventTypes(body.EventTypes, out string? eventError);
            if (eventError != null) return BadRequest(eventError);
        }

        string? url = null;
        if (body.Url != null) {
            (url, ActionResult? urlError) = await ValidateUrl(body.Url);
            if (urlError != null) return urlError;
        }

        AppWebhookInfo? updated = await webhookRepo.Update(app!.Id, webhookId, new AppWebhookUpdate {
            Url = url,
            EventTypes = eventTypes,
            Enabled = body.Enabled
        });
        return updated == null ? NotFound() : Ok(AppWebhookController.WebhookView.From(updated));
    }

    [HttpDelete("{webhookId}")]
    public async Task<IActionResult> Delete(string appid, string webhookId) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        bool deleted = await webhookRepo.Delete(app!.Id, webhookId);
        if (!deleted) return NotFound();
        logger.LogInformation("User {UserId} deleted webhook {WebhookId} for app {AppId}",
            HttpContext.User.GetUserId(), webhookId, app.Id);
        return Ok(new { success = true });
    }

    /// <summary>Issues a new signing secret. The previous one stops verifying immediately.</summary>
    [HttpPost("{webhookId}/rotate-secret")]
    public async Task<ActionResult<AppWebhookController.WebhookWithSecretView>> RotateSecret(
        string appid, string webhookId) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        AppWebhookWithSecret? rotated = await webhookRepo.RotateSecret(app!.Id, webhookId);
        if (rotated == null) return NotFound();
        logger.LogInformation("User {UserId} rotated the secret for webhook {WebhookId} of app {AppId}",
            HttpContext.User.GetUserId(), webhookId, app.Id);
        return Ok(AppWebhookController.WebhookWithSecretView.From(rotated));
    }

    /// <summary>
    /// Queues a <c>webhook.test</c> event so an integration can be verified end to end without
    /// waiting for a tax cycle.
    /// </summary>
    // Queues an outbound request to a URL the caller chose.
    [RateLimit(RateLimitTiers.Costly)]
    [HttpPost("{webhookId}/test")]
    public async Task<IActionResult> Test(string appid, string webhookId) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;

        string? deliveryId = await webhookRepo.EnqueueTest(app!.Id, webhookId);
        return deliveryId == null
            ? NotFound("No enabled webhook with that id.")
            : Ok(new { queued = true, deliveryId });
    }

    [HttpGet("deliveries")]
    public Task<ActionResult<AppWebhookController.DeliveryPageView>> ListDeliveries(
        string appid, [FromQuery] int skip = 0, [FromQuery] int take = 50) =>
        Deliveries(appid, null, skip, take);

    [HttpGet("{webhookId}/deliveries")]
    public Task<ActionResult<AppWebhookController.DeliveryPageView>> ListWebhookDeliveries(
        string appid, string webhookId, [FromQuery] int skip = 0, [FromQuery] int take = 50) =>
        Deliveries(appid, webhookId, skip, take);

    private async Task<ActionResult<AppWebhookController.DeliveryPageView>> Deliveries(
        string appid, string? webhookId, int skip, int take) {
        (OAuthApp? app, ActionResult? error) = await GetOwnedApp(appid);
        if (error != null) return error;
        if (skip < 0) return BadRequest("skip must not be negative.");
        if (take is < 1 or > 200) return BadRequest("take must be between 1 and 200.");

        WebhookDeliveryPage page = await webhookRepo.GetDeliveries(app!.Id, webhookId, skip, take);
        return Ok(new AppWebhookController.DeliveryPageView {
            TotalCount = page.TotalCount,
            Deliveries = page.Deliveries.Select(AppWebhookController.DeliveryView.From).ToList()
        });
    }

    // ---------------------------------------------------------------------------------------

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
