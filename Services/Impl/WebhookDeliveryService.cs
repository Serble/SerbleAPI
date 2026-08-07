using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;

namespace SerbleAPI.Services.Impl;

/// <summary>
/// The webhook dispatcher's working half: claim, send, record.
///
/// <para><b>Multi-replica safety.</b> Claiming is a conditional
/// <c>UPDATE … WHERE Status = Pending</c> over a bounded set of ids, and the affected-row count is
/// the arbiter — whichever replica's update lands first owns those rows, and the loser reads back
/// nothing. That is cheaper than an advisory lock and, unlike one, lets every replica dispatch at
/// once instead of idling behind a single worker.</para>
///
/// <para><b>Failure model.</b> Attempts are retried with exponential backoff and jitter, then
/// dead-lettered. A subscription that fails for long enough is disabled rather than retried
/// forever, because a decommissioned endpoint would otherwise consume a share of every batch
/// indefinitely.</para>
/// </summary>
public class WebhookDeliveryService(
    SerbleDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDeliveryService> logger) : IWebhookDeliveryService {

    /// <summary>Named client registered in <c>Program.cs</c> with the per-request timeout.</summary>
    public const string HttpClientName = "webhooks";

    /// <summary>Attempts before a delivery is dead-lettered. With the backoff below this spans roughly a day.</summary>
    private const int MaxAttempts = 8;

    /// <summary>First retry delay. Each subsequent attempt quadruples it, up to <see cref="MaxBackoff"/>.</summary>
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    /// <summary>How long a claim may sit untouched before another instance may take it back.</summary>
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Failed attempts in a row before a subscription is switched off. Deliberately several
    /// dead-letters' worth, so a subscriber having a bad day is not unsubscribed for it.
    /// </summary>
    private const int AutoDisableAfterFailures = 50;

    /// <summary>In-flight POSTs per batch. Caps sockets and DB-free wall time without serialising slow endpoints.</summary>
    private const int MaxConcurrency = 8;

    /// <summary>Longest response body kept in <c>LastError</c>, to keep a chatty endpoint from bloating the row.</summary>
    private const int MaxRecordedErrorLength = 480;

    private static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    private sealed record Attempt(string DeliveryId, bool Success, int? StatusCode, string? Error);

    public async Task<int> DispatchDue(int batchSize, CancellationToken cancellationToken = default) {
        batchSize = Math.Clamp(batchSize, 1, 500);
        int pending = (int)WebhookDeliveryStatus.Pending;
        int inFlight = (int)WebhookDeliveryStatus.InFlight;
        DateTime now = DateTime.UtcNow;

        db.ChangeTracker.Clear();

        List<string> candidateIds = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.Status == pending && d.NextAttemptUtc <= now)
            .OrderBy(d => d.NextAttemptUtc)
            .Take(batchSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0) return 0;

        // The claim. Rows another replica took between the read above and this update no longer
        // match Status = Pending, so they are simply not ours.
        int claimed = await db.WebhookDeliveries
            .Where(d => candidateIds.Contains(d.Id) && d.Status == pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, inFlight)
                .SetProperty(d => d.ClaimedBy, InstanceId)
                .SetProperty(d => d.ClaimedUtc, now), cancellationToken);
        if (claimed == 0) return 0;

        List<DbWebhookDelivery> batch = await db.WebhookDeliveries
            .Where(d => candidateIds.Contains(d.Id) && d.Status == inFlight && d.ClaimedBy == InstanceId)
            .ToListAsync(cancellationToken);
        if (batch.Count == 0) return 0;

        string[] webhookIds = batch.Select(d => d.WebhookId).Distinct().ToArray();
        Dictionary<string, DbAppWebhook> hooks = await db.AppWebhooks
            .Where(w => webhookIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        Attempt[] results = await Send(batch, hooks, cancellationToken);
        await Record(batch, hooks, results, cancellationToken);
        return batch.Count;
    }

    public async Task<int> ReleaseStaleClaims(CancellationToken cancellationToken = default) {
        int inFlight = (int)WebhookDeliveryStatus.InFlight;
        DateTime cutoff = DateTime.UtcNow - StaleClaimAge;

        int released = await db.WebhookDeliveries
            .Where(d => d.Status == inFlight && (d.ClaimedUtc == null || d.ClaimedUtc < cutoff))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, (int)WebhookDeliveryStatus.Pending)
                .SetProperty(d => d.ClaimedBy, (string?)null)
                .SetProperty(d => d.ClaimedUtc, (DateTime?)null), cancellationToken);

        if (released > 0) {
            logger.LogWarning("Released {Count} abandoned webhook delivery claim(s) back to the queue", released);
        }
        return released;
    }

    // ---------------------------------------------------------------------------------------
    // Sending
    // ---------------------------------------------------------------------------------------

    private async Task<Attempt[]> Send(
        List<DbWebhookDelivery> batch,
        Dictionary<string, DbAppWebhook> hooks,
        CancellationToken cancellationToken) {

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using SemaphoreSlim gate = new(MaxConcurrency);

        IEnumerable<Task<Attempt>> sends = batch.Select(async delivery => {
            await gate.WaitAsync(cancellationToken);
            try {
                if (!hooks.TryGetValue(delivery.WebhookId, out DbAppWebhook? hook)) {
                    // The subscription was deleted between enqueue and dispatch. Nothing to send to,
                    // and nothing to retry into.
                    return new Attempt(delivery.Id, false, null, "Subscription no longer exists.");
                }
                return await Post(client, delivery, hook, cancellationToken);
            }
            finally {
                gate.Release();
            }
        });

        return await Task.WhenAll(sends);
    }

    private async Task<Attempt> Post(
        HttpClient client,
        DbWebhookDelivery delivery,
        DbAppWebhook hook,
        CancellationToken cancellationToken) {

        try {
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            using HttpRequestMessage request = new(HttpMethod.Post, hook.Url);
            request.Content = new StringContent(delivery.Payload, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.TryAddWithoutValidation("X-Serble-Event", delivery.EventType);
            request.Headers.TryAddWithoutValidation("X-Serble-Delivery", delivery.Id);
            request.Headers.TryAddWithoutValidation("X-Serble-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-Serble-Attempt", (delivery.Attempts + 1).ToString());
            request.Headers.TryAddWithoutValidation("X-Serble-Signature", Sign(hook.Secret, timestamp, delivery.Payload));
            request.Headers.UserAgent.ParseAdd("Serble-Webhooks/1.0");

            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            int status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode) return new Attempt(delivery.Id, true, status, null);

            string body = await ReadErrorBody(response, cancellationToken);
            return new Attempt(delivery.Id, false, status, $"HTTP {status} {response.ReasonPhrase}: {body}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (TaskCanceledException) {
            // Cancellation was not requested, so this is the client's own timeout.
            return new Attempt(delivery.Id, false, null, "The request timed out.");
        }
        catch (HttpRequestException ex) {
            return new Attempt(delivery.Id, false, null, ex.Message);
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "Unexpected failure delivering webhook {DeliveryId} to app {AppId}",
                delivery.Id, delivery.AppId);
            return new Attempt(delivery.Id, false, null, ex.Message);
        }
    }

    private static async Task<string> ReadErrorBody(HttpResponseMessage response, CancellationToken cancellationToken) {
        try {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= 200 ? body : body[..200];
        }
        catch {
            return "";
        }
    }

    /// <summary>
    /// HMAC-SHA256 over <c>timestamp + "." + body</c>. Binding the timestamp into the signed input
    /// is what lets a receiver reject a replayed request: the signature only stays valid for the
    /// timestamp it was made with, so an old capture cannot be re-timestamped.
    /// </summary>
    private static string Sign(string secret, string timestamp, string body) {
        byte[] signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexString(signature).ToLowerInvariant();
    }

    // ---------------------------------------------------------------------------------------
    // Recording
    // ---------------------------------------------------------------------------------------

    private async Task Record(
        List<DbWebhookDelivery> batch,
        Dictionary<string, DbAppWebhook> hooks,
        Attempt[] results,
        CancellationToken cancellationToken) {

        Dictionary<string, Attempt> byId = results.ToDictionary(r => r.DeliveryId);
        DateTime now = DateTime.UtcNow;

        foreach (DbWebhookDelivery delivery in batch) {
            if (!byId.TryGetValue(delivery.Id, out Attempt? attempt)) continue;

            delivery.Attempts++;
            delivery.LastResponseCode = attempt.StatusCode;
            delivery.ClaimedBy = null;
            delivery.ClaimedUtc = null;
            hooks.TryGetValue(delivery.WebhookId, out DbAppWebhook? hook);

            if (attempt.Success) {
                delivery.Status = (int)WebhookDeliveryStatus.Delivered;
                delivery.DeliveredUtc = now;
                delivery.LastError = null;
                if (hook != null) {
                    hook.ConsecutiveFailures = 0;
                    hook.LastSuccessUtc = now;
                    hook.LastError = null;
                }
                continue;
            }

            delivery.LastError = Truncate(attempt.Error, MaxRecordedErrorLength);

            if (hook == null) {
                // No subscription to deliver to; retrying cannot help.
                delivery.Status = (int)WebhookDeliveryStatus.DeadLettered;
                continue;
            }

            hook.ConsecutiveFailures++;
            hook.LastFailureUtc = now;
            hook.LastError = delivery.LastError;

            if (delivery.Attempts >= MaxAttempts) {
                delivery.Status = (int)WebhookDeliveryStatus.DeadLettered;
                logger.LogWarning(
                    "Webhook delivery {DeliveryId} ({EventType}) to app {AppId} dead-lettered after {Attempts} attempts: {Error}",
                    delivery.Id, delivery.EventType, delivery.AppId, delivery.Attempts, delivery.LastError);
            }
            else {
                delivery.Status = (int)WebhookDeliveryStatus.Pending;
                delivery.NextAttemptUtc = now + BackoffFor(delivery.Attempts);
            }

            if (hook.Enabled && hook.ConsecutiveFailures >= AutoDisableAfterFailures) {
                hook.Enabled = false;
                hook.DisabledReason =
                    $"Automatically disabled after {hook.ConsecutiveFailures} consecutive failed deliveries. " +
                    "Fix the endpoint and re-enable the webhook to resume.";
                hook.DateUpdated = now;
                logger.LogWarning("Auto-disabled webhook {WebhookId} for app {AppId} after {Failures} consecutive failures",
                    hook.Id, hook.AppId, hook.ConsecutiveFailures);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Quadrupling backoff, capped, with up to 20% jitter. The jitter matters more than the curve:
    /// a whole batch queued by one tax cycle would otherwise retry in lockstep and hit a recovering
    /// endpoint with the same thundering herd that knocked it over.
    /// </summary>
    private static TimeSpan BackoffFor(int attempts) {
        double seconds = BaseBackoff.TotalSeconds * Math.Pow(4, Math.Max(0, attempts - 1));
        seconds = Math.Min(seconds, MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds * (1 + RandomNumberGenerator.GetInt32(0, 200) / 1000d));
    }

    private static string? Truncate(string? value, int maxLength) =>
        value == null || value.Length <= maxLength ? value : value[..maxLength];
}
