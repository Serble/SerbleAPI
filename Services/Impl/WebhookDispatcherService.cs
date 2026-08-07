namespace SerbleAPI.Services.Impl;

/// <summary>
/// Drives webhook delivery, separately from <see cref="TaxBackgroundService"/> on purpose: tax
/// holds economy row locks while it works, and delivery waits on other people's web servers. Tying
/// the two together would let the second stall the first.
/// <para>
/// Every replica runs this and they cooperate through the claim in
/// <see cref="WebhookDeliveryService"/>, so throughput scales with replica count rather than being
/// pinned to whichever instance holds a lock.
/// </para>
/// </summary>
public class WebhookDispatcherService(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDispatcherService> logger) : BackgroundService {

    /// <summary>Idle poll interval. Short enough that a webhook feels immediate, long enough to be a cheap index probe.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often abandoned claims are swept back onto the queue.</summary>
    private static readonly TimeSpan StaleSweepInterval = TimeSpan.FromMinutes(1);

    private const int BatchSize = 50;

    /// <summary>
    /// Batches per tick. A backlog drains within one tick instead of at one batch per five seconds,
    /// but the bound keeps a large queue from monopolising the loop and starving the sweep.
    /// </summary>
    private const int MaxBatchesPerTick = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using PeriodicTimer timer = new(PollInterval);
        DateTime nextSweep = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                using IServiceScope scope = scopeFactory.CreateScope();
                IWebhookDeliveryService deliveries = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();

                if (DateTime.UtcNow >= nextSweep) {
                    await deliveries.ReleaseStaleClaims(stoppingToken);
                    nextSweep = DateTime.UtcNow + StaleSweepInterval;
                }

                for (int i = 0; i < MaxBatchesPerTick; i++) {
                    int processed = await deliveries.DispatchDue(BatchSize, stoppingToken);
                    if (processed < BatchSize) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Webhook dispatcher pass failed");
            }

            try {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
        }
    }
}
