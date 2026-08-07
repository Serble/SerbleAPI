using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Models;
using SerbleAPI.Services.Impl;

namespace SerbleAPI.Repositories.Impl;

public class AppWebhookRepository(SerbleDbContext db) : IAppWebhookRepository {

    /// <summary>Prefix on the signing secret so a leaked string is recognisable in a log or a paste.</summary>
    private const string SecretPrefix = "swhs_";

    /// <summary>Bytes of entropy behind a signing secret. 32 matches the HMAC-SHA256 block it keys.</summary>
    private const int SecretBytes = 32;

    private static AppWebhookInfo MapInfo(DbAppWebhook r) => new() {
        Id                  = r.Id,
        AppId               = r.AppId,
        Url                 = r.Url,
        EventTypes          = WebhookEventTypes.Parse(r.EventTypes),
        Enabled             = r.Enabled,
        DisabledReason      = r.DisabledReason,
        ConsecutiveFailures = r.ConsecutiveFailures,
        LastSuccessUtc      = r.LastSuccessUtc,
        LastFailureUtc      = r.LastFailureUtc,
        LastError           = r.LastError,
        DateCreated         = r.DateCreated,
        DateUpdated         = r.DateUpdated
    };

    private static WebhookDeliveryInfo MapDelivery(DbWebhookDelivery r) => new() {
        Id               = r.Id,
        WebhookId        = r.WebhookId,
        AppId            = r.AppId,
        EventType        = r.EventType,
        CycleId          = r.CycleId,
        Status           = (WebhookDeliveryStatus)r.Status,
        Attempts         = r.Attempts,
        CreatedUtc       = r.CreatedUtc,
        NextAttemptUtc   = r.NextAttemptUtc,
        DeliveredUtc     = r.DeliveredUtc,
        LastResponseCode = r.LastResponseCode,
        LastError        = r.LastError,
        Payload          = r.Payload
    };

    private static string NewSecret() =>
        SecretPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretBytes));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<AppWebhookInfo[]> GetForApp(string appId) {
        DbAppWebhook[] rows = await db.AppWebhooks.AsNoTracking()
            .Where(w => w.AppId == appId)
            .OrderBy(w => w.DateCreated)
            .ToArrayAsync();
        return rows.Select(MapInfo).ToArray();
    }

    public async Task<AppWebhookInfo?> Get(string appId, string webhookId) {
        DbAppWebhook? row = await db.AppWebhooks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.AppId == appId && w.Id == webhookId);
        return row == null ? null : MapInfo(row);
    }

    public Task<int> CountForApp(string appId) =>
        db.AppWebhooks.AsNoTracking().CountAsync(w => w.AppId == appId);

    public async Task<AppWebhookWithSecret> Create(string appId, string url, IEnumerable<string> eventTypes) {
        DateTime now = DateTime.UtcNow;
        string secret = NewSecret();
        DbAppWebhook row = new() {
            Id          = Guid.NewGuid().ToString(),
            AppId       = appId,
            Url         = url,
            Secret      = secret,
            EventTypes  = WebhookEventTypes.Join(eventTypes),
            Enabled     = true,
            DateCreated = now,
            DateUpdated = now
        };
        db.AppWebhooks.Add(row);
        await db.SaveChangesAsync();
        return new AppWebhookWithSecret { Info = MapInfo(row), Secret = secret };
    }

    public async Task<AppWebhookInfo?> Update(string appId, string webhookId, AppWebhookUpdate update) {
        DbAppWebhook? row = await db.AppWebhooks.FirstOrDefaultAsync(w => w.AppId == appId && w.Id == webhookId);
        if (row == null) return null;

        if (update.Url != null) row.Url = update.Url;
        if (update.EventTypes != null) row.EventTypes = WebhookEventTypes.Join(update.EventTypes);
        if (update.Enabled != null && update.Enabled.Value != row.Enabled) {
            row.Enabled = update.Enabled.Value;
            if (row.Enabled) {
                // Re-enabling is the owner saying the endpoint is fixed. Carrying the old failure
                // count forward would trip auto-disable again on the very next attempt.
                row.ConsecutiveFailures = 0;
                row.DisabledReason = null;
            }
        }

        row.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return MapInfo(row);
    }

    public async Task<bool> Delete(string appId, string webhookId) {
        int deleted = await db.AppWebhooks
            .Where(w => w.AppId == appId && w.Id == webhookId)
            .ExecuteDeleteAsync();
        return deleted > 0;
    }

    public async Task<AppWebhookWithSecret?> RotateSecret(string appId, string webhookId) {
        DbAppWebhook? row = await db.AppWebhooks.FirstOrDefaultAsync(w => w.AppId == appId && w.Id == webhookId);
        if (row == null) return null;

        string secret = NewSecret();
        row.Secret = secret;
        row.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new AppWebhookWithSecret { Info = MapInfo(row), Secret = secret };
    }

    public async Task<string?> EnqueueTest(string appId, string webhookId) {
        bool exists = await db.AppWebhooks.AsNoTracking()
            .AnyAsync(w => w.AppId == appId && w.Id == webhookId && w.Enabled);
        if (!exists) return null;

        WebhookEvent evt = new() {
            AppId = appId,
            EventType = WebhookEventTypes.Test,
            ForcedWebhookId = webhookId,
            Payload = new WebhookEventPayload {
                Message = "Test event from Serble. If you can verify this signature, your endpoint is configured correctly."
            }
        };
        List<DbWebhookDelivery> queued = await WebhookOutbox.Enqueue(db, [evt]);
        if (queued.Count == 0) return null;

        await db.SaveChangesAsync();
        return queued[0].Id;
    }

    public async Task<WebhookDeliveryPage> GetDeliveries(string appId, string? webhookId, int skip, int take) {
        IQueryable<DbWebhookDelivery> query = db.WebhookDeliveries.AsNoTracking().Where(d => d.AppId == appId);
        if (webhookId != null) query = query.Where(d => d.WebhookId == webhookId);
        return await Page(query, skip, take);
    }

    public async Task<AppWebhookInfo[]> GetAll(string? appId, int skip, int take) {
        IQueryable<DbAppWebhook> query = db.AppWebhooks.AsNoTracking();
        if (appId != null) query = query.Where(w => w.AppId == appId);
        DbAppWebhook[] rows = await query
            .OrderByDescending(w => w.DateCreated)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync();
        return rows.Select(MapInfo).ToArray();
    }

    public Task<long> CountAll(string? appId) {
        IQueryable<DbAppWebhook> query = db.AppWebhooks.AsNoTracking();
        if (appId != null) query = query.Where(w => w.AppId == appId);
        return query.LongCountAsync();
    }

    public async Task<WebhookDeliveryPage> GetAllDeliveries(
        string? appId, WebhookDeliveryStatus? status, long? cycleId, int skip, int take) {

        IQueryable<DbWebhookDelivery> query = db.WebhookDeliveries.AsNoTracking();
        if (appId != null) query = query.Where(d => d.AppId == appId);
        if (status != null) {
            int raw = (int)status.Value;
            query = query.Where(d => d.Status == raw);
        }
        if (cycleId != null) query = query.Where(d => d.CycleId == cycleId);
        return await Page(query, skip, take);
    }

    public async Task<bool> Redeliver(string deliveryId) {
        int updated = await db.WebhookDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, (int)WebhookDeliveryStatus.Pending)
                .SetProperty(d => d.Attempts, 0)
                .SetProperty(d => d.NextAttemptUtc, DateTime.UtcNow)
                .SetProperty(d => d.ClaimedBy, (string?)null)
                .SetProperty(d => d.ClaimedUtc, (DateTime?)null));
        return updated > 0;
    }

    private static async Task<WebhookDeliveryPage> Page(IQueryable<DbWebhookDelivery> query, int skip, int take) {
        long total = await query.LongCountAsync();
        List<DbWebhookDelivery> rows = await query
            .OrderByDescending(d => d.CreatedUtc)
            .ThenByDescending(d => d.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return new WebhookDeliveryPage {
            Deliveries = rows.Select(MapDelivery).ToList(),
            TotalCount = total
        };
    }
}
